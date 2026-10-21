using System.Text.Json;
using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using ExamPrep.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Controllers;

[Authorize]
public class PracticeController(AppDbContext db, UserManager<ApplicationUser> users, PracticeService practice) : Controller
{
    private string UserId => users.GetUserId(User)!;

    [AllowAnonymous]
    public async Task<IActionResult> Index(string? exam, int? year, string? q, int page = 1)
    {
        var papers = db.Papers.AsNoTracking();
        if (!string.IsNullOrEmpty(exam)) papers = papers.Where(p => p.Exam!.Slug == exam);
        if (year is not null) papers = papers.Where(p => p.RocYear == year);
        if (!string.IsNullOrWhiteSpace(q)) papers = papers.Where(p => p.SubjectName.Contains(q) || p.Categories.Contains(q));

        var userId = users.GetUserId(User);
        var total = await papers.CountAsync();
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PracticeIndexVm.PageSize));
        page = Math.Clamp(page, 1, pages);
        var rows = await papers.OrderByDescending(p => p.RocYear).ThenBy(p => p.Exam!.SortOrder).ThenBy(p => p.SubjectName).ThenBy(p => p.Id)
            .Skip((page - 1) * PracticeIndexVm.PageSize).Take(PracticeIndexVm.PageSize)
            .Select(p => new PaperRow
            {
                Id = p.Id, RocYear = p.RocYear, ExamTitle = p.ExamTitle, SubjectName = p.SubjectName,
                Categories = p.Categories, PaperCode = p.PaperCode, QuestionCount = p.Questions.Count,
                OfficialCount = p.OfficialQuestionCount, ExamSlug = p.Exam != null ? p.Exam.Slug : null,
                Withheld = p.ContentWithheld
            }).ToListAsync();

        if (userId is not null)
        {
            // 每份試卷的最佳成績：整卷練習的場次 PaperId 對應
            var ids = rows.Select(r => r.Id).ToList();
            var bestMap = await db.PracticeSessions
                .Where(s => s.UserId == userId && s.FinishedAt != null && s.TotalCount > 0 && s.PaperId != null && ids.Contains(s.PaperId.Value))
                .GroupBy(s => s.PaperId!.Value)
                .Select(g => new { g.Key, Pct = g.Max(s => s.CorrectCount * 100 / s.TotalCount) })
                .ToDictionaryAsync(x => x.Key, x => x.Pct);
            foreach (var r in rows)
                if (bestMap.TryGetValue(r.Id, out var pct)) r.BestScore = pct;
        }

        var subjects = await db.Questions.GroupBy(x => x.Paper!.SubjectName)
            .Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync();

        return View(new PracticeIndexVm
        {
            Exam = exam, Year = year, Q = q, Page = page, PageCount = pages, Total = total,
            ExamPaperCounts = await db.Papers.Where(p => p.Exam != null).GroupBy(p => p.Exam!.Slug).Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C),
            AllPaperCount = await db.Papers.CountAsync(),
            Exams = await db.Exams.OrderBy(e => e.SortOrder).ToListAsync(),
            Years = await db.Papers.Select(p => p.RocYear).Distinct().OrderByDescending(y => y).ToListAsync(),
            Papers = rows,
            Subjects = subjects.Select(s => (s.Key, s.Count)).ToList()
        });
    }

    /// <summary>整卷作答。mode：normal 一般交卷批改；instant 逐題即時對答；exam 計時模擬考。</summary>
    [HttpPost]
    public async Task<IActionResult> StartPaper(int paperId, string mode = "normal", int minutes = 60)
    {
        var paper = await db.Papers.FirstOrDefaultAsync(p => p.Id == paperId);
        if (paper is null) return NotFound();
        if (paper.ContentWithheld)
        {
            TempData["Flash"] = "公開版不提供這份試卷的題目內文，請由試卷頁的原始來源連結查看。";
            return RedirectToAction(nameof(Paper), new { id = paperId });
        }
        var s = await practice.CreateFromPaperAsync(UserId, paperId, mode == "instant", mode == "exam" ? minutes : null);
        return RedirectToAction(nameof(Take), new { id = s.Id });
    }

    [HttpPost]
    public async Task<IActionResult> StartRandom(string subject, int count = 20, bool unseenOnly = false, bool instant = false)
    {
        var s = await practice.CreateRandomAsync(UserId, subject, count, unseenOnly, instant);
        if (s is null)
        {
            TempData["Flash"] = unseenOnly ? $"「{subject}」的題目你都做過了，可取消「只出沒做過的題目」。" : $"找不到「{subject}」的題目。";
            return RedirectToAction(nameof(Index));
        }
        return RedirectToAction(nameof(Take), new { id = s.Id });
    }

    /// <summary>自訂範圍出題頁。</summary>
    public async Task<IActionResult> Custom(string? exam)
    {
        var subjects = db.Questions.Where(q => !q.Paper!.ContentWithheld);
        if (!string.IsNullOrEmpty(exam)) subjects = subjects.Where(q => q.Paper!.Exam!.Slug == exam);
        return View(new CustomVm
        {
            Exam = exam,
            Exams = await db.Exams.OrderBy(e => e.SortOrder).ToListAsync(),
            Years = await db.Papers.Where(p => !p.ContentWithheld).Select(p => p.RocYear).Distinct().OrderBy(y => y).ToListAsync(),
            Subjects = (await subjects.GroupBy(q => q.Paper!.SubjectName).Select(g => new { g.Key, C = g.Count() })
                .OrderByDescending(x => x.C).ToListAsync()).Select(x => (x.Key, x.C)).ToList()
        });
    }

    [HttpPost]
    public async Task<IActionResult> StartCustom(CustomInput input)
    {
        var query = new CustomQuery(input.Exam, input.YearFrom, input.YearTo, input.Subjects ?? [], input.Count,
            input.Source, input.Order == "sequential");
        var s = await practice.CreateCustomAsync(UserId, query, input.Mode == "instant", input.Mode == "exam" ? input.Minutes : null);
        if (s is null)
        {
            TempData["Flash"] = "沒有符合條件的題目，請放寬年度、科目或題目來源。";
            return RedirectToAction(nameof(Custom), new { exam = input.Exam });
        }
        return RedirectToAction(nameof(Take), new { id = s.Id });
    }

    [HttpPost]
    public async Task<IActionResult> StartReview(int count = 20)
    {
        var s = await practice.CreateReviewAsync(UserId, count);
        if (s is null)
        {
            TempData["Flash"] = "今天沒有到期要複習的錯題。";
            return RedirectToAction("Index", "WrongBook");
        }
        return RedirectToAction(nameof(Take), new { id = s.Id });
    }

    [HttpPost]
    public async Task<IActionResult> StartDaily()
    {
        var goal = (await db.UserSettings.FindAsync(UserId))?.DailyGoalQuestions ?? 20;
        var s = await practice.CreateDailyAsync(UserId, goal);
        if (s is null)
        {
            TempData["Flash"] = "題庫裡的題目你都做過了，可以改用錯題重練或自訂練習。";
            return RedirectToAction("Index", "Home");
        }
        return RedirectToAction(nameof(Take), new { id = s.Id });
    }

    /// <summary>逐題模式的單題對答（JSON）。</summary>
    [HttpPost]
    public async Task<IActionResult> Check(int id, int questionId, string? selected)
    {
        var s = await db.PracticeSessions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (s is null) return NotFound();
        var a = await practice.AnswerOneAsync(s, questionId, selected);
        if (a is null) return BadRequest();
        var answer = await db.Questions.Where(q => q.Id == questionId).Select(q => q.Answer).FirstAsync();
        return Json(new { correct = a.IsCorrect, selected = a.Selected, answer, finished = s.FinishedAt is not null });
    }

    /// <summary>我的收藏。</summary>
    public async Task<IActionResult> Bookmarks()
    {
        var list = await db.Bookmarks.Where(b => b.UserId == UserId && !b.Question!.Paper!.ContentWithheld)
            .Include(b => b.Question).ThenInclude(q => q!.Paper)
            .OrderByDescending(b => b.CreatedAt).Take(300).ToListAsync();
        return View(list);
    }

    public async Task<IActionResult> Take(int id)
    {
        var s = await db.PracticeSessions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (s is null) return NotFound();
        if (s.FinishedAt is not null) return RedirectToAction(nameof(Result), new { id });
        await db.Entry(s).Collection(x => x.Answers).LoadAsync();
        return View(new TakeVm
        {
            Session = s,
            Questions = await practice.LoadQuestionsAsync(s),
            Answers = s.Answers.ToDictionary(a => a.QuestionId),
            Deadline = s.TimeLimitMinutes is { } m ? DateTime.SpecifyKind(s.StartedAt, DateTimeKind.Utc).AddMinutes(m) : null
        });
    }

    [HttpPost]
    public async Task<IActionResult> Submit(int id, IFormCollection form)
    {
        var s = await db.PracticeSessions.Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (s is null) return NotFound();
        var selections = new Dictionary<int, string>();
        foreach (var key in form.Keys.Where(k => k.StartsWith("q_")))
            if (int.TryParse(key[2..], out var qid))
                selections[qid] = string.Concat(form[key].Select(v => v ?? ""));
        await practice.GradeAsync(s, selections);
        return RedirectToAction(nameof(Result), new { id });
    }

    public async Task<IActionResult> Result(int id)
    {
        var s = await db.PracticeSessions.Include(x => x.Answers).FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (s is null) return NotFound();
        if (s.FinishedAt is null) return RedirectToAction(nameof(Take), new { id });
        var questions = await practice.LoadQuestionsAsync(s);
        var ids = questions.Select(q => q.Id).ToList();
        return View(new TakeVm
        {
            Session = s,
            Questions = questions,
            Answers = s.Answers.ToDictionary(a => a.QuestionId),
            InWrongBook = (await db.WrongQuestions.Where(w => w.UserId == UserId && !w.Mastered && ids.Contains(w.QuestionId))
                .Select(w => w.QuestionId).ToListAsync()).ToHashSet()
        });
    }

    public async Task<IActionResult> History()
    {
        var list = await db.PracticeSessions.Where(s => s.UserId == UserId)
            .OrderByDescending(s => s.StartedAt).Take(100).ToListAsync();
        return View(list);
    }

    [AllowAnonymous]
    public async Task<IActionResult> Paper(int id)
    {
        var p = await db.Papers.Include(x => x.Exam).AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (p is null) return NotFound();
        ViewBag.Excluded = JsonSerializer.Deserialize<List<SeedDataService.ExcludedSeed>>(p.ExcludedJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        ViewBag.Count = await db.Questions.CountAsync(q => q.PaperId == id);
        ViewBag.ZipCount = p.ContentWithheld ? 0 : await db.Papers.CountAsync(x => x.SubjectName == p.SubjectName && x.ExamId == p.ExamId
                                                                                 && !x.ContentWithheld && x.SourceQuestionUrl.StartsWith("https://wwwq.moex.gov.tw/"));
        return View(p);
    }
}
