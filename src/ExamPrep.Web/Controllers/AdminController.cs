using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using ExamPrep.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Controllers;

[Authorize(Roles = AppInitializer.AdminRole)]
public class AdminController(AppDbContext db, QuestionCsvImporter csv, SeedDataService seed) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Papers = await db.Papers.CountAsync();
        ViewBag.Questions = await db.Questions.CountAsync();
        ViewBag.Users = await db.Users.CountAsync();
        var sessions = await db.ExamSessions.Include(s => s.Exam).OrderByDescending(s => s.ExamStart).ToListAsync();
        var custom = await db.Papers.Where(p => !p.SourceQuestionUrl.Contains("moex.gov.tw"))
            .OrderByDescending(p => p.ImportedAt).Select(p => new PaperRow
            {
                Id = p.Id, RocYear = p.RocYear, ExamTitle = p.ExamTitle, SubjectName = p.SubjectName,
                PaperCode = p.PaperCode, QuestionCount = p.Questions.Count
            }).ToListAsync();
        ViewBag.Custom = custom;
        return View(sessions);
    }

    [HttpPost]
    public async Task<IActionResult> ReimportOfficial()
    {
        var info = await seed.ImportExamInfoAsync();
        var r = await seed.ImportOfficialPapersAsync();
        TempData["Flash"] = $"考試資訊已更新（{info.ExamsUpserted} 項）；官方試題新增 {r.PapersAdded} 份、{r.QuestionsAdded} 題，略過既有 {r.PapersSkipped} 份。";
        return RedirectToAction(nameof(Index));
    }

    // ---------- CSV 匯入 ----------
    [HttpGet]
    public async Task<IActionResult> ImportCsv() => View(new CsvImportVm { Exams = await db.Exams.OrderBy(e => e.SortOrder).ToListAsync() });

    [HttpPost, RequestSizeLimit(5_000_000)]
    public async Task<IActionResult> ImportCsv(CsvImportVm vm, IFormFile? file)
    {
        vm.Exams = await db.Exams.OrderBy(e => e.SortOrder).ToListAsync();
        if (file is null || file.Length == 0) ModelState.AddModelError("", "請選擇 CSV 檔案。");
        if (!ModelState.IsValid) return View(vm);

        var code = $"CUSTOM-{vm.ExamId?.ToString() ?? "0"}-{vm.RocYear}";
        if (await db.Papers.AnyAsync(p => p.ExamCode == code && p.PaperCode == vm.PaperCode))
        {
            ModelState.AddModelError(nameof(vm.PaperCode), "同一考試、年度已有相同代號的試卷。");
            return View(vm);
        }

        await using var stream = file!.OpenReadStream();
        var result = csv.Parse(stream);
        if (!result.Ok)
        {
            vm.Errors = result.Errors.Count > 0 ? result.Errors : ["檔案中沒有任何題目。"];
            return View(vm);
        }
        var paper = new QuestionPaper
        {
            ExamCode = code,
            PaperCode = vm.PaperCode.Trim(),
            ExamId = vm.ExamId,
            ExamTitle = vm.ExamTitle.Trim(),
            RocYear = vm.RocYear,
            Categories = vm.Categories?.Trim() ?? "",
            SubjectName = vm.SubjectName.Trim(),
            SourceQuestionUrl = vm.SourceUrl.Trim(),
            SourceAnswerUrl = vm.AnswerSourceUrl?.Trim() ?? vm.SourceUrl.Trim(),
            OfficialQuestionCount = result.Questions.Count,
            ImportedAt = DateTime.UtcNow,
            Questions = result.Questions
        };
        db.Papers.Add(paper);
        await db.SaveChangesAsync();
        TempData["Flash"] = $"已匯入「{paper.SubjectName}」共 {paper.Questions.Count} 題。";
        return RedirectToAction("Paper", "Practice", new { id = paper.Id });
    }

    public IActionResult CsvTemplate()
    {
        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        var header = string.Join(",", QuestionCsvImporter.Headers) + "\r\n";
        return File(bom.Concat(System.Text.Encoding.UTF8.GetBytes(header)).ToArray(), "text/csv", "題目匯入範本.csv");
    }

    [HttpPost]
    public async Task<IActionResult> DeletePaper(int id)
    {
        var p = await db.Papers.FirstOrDefaultAsync(x => x.Id == id && !x.SourceQuestionUrl.Contains("moex.gov.tw"));
        if (p is null) return NotFound();
        var qids = db.Questions.Where(q => q.PaperId == id).Select(q => q.Id);
        db.PracticeAnswers.RemoveRange(db.PracticeAnswers.Where(a => qids.Contains(a.QuestionId)));
        db.Papers.Remove(p);
        await db.SaveChangesAsync();
        TempData["Flash"] = $"已刪除試卷「{p.SubjectName}」。";
        return RedirectToAction(nameof(Index));
    }

    // ---------- 考期維護 ----------
    [HttpGet]
    public async Task<IActionResult> EditSession(int? id, int? examId)
    {
        ViewBag.Exams = await db.Exams.OrderBy(e => e.SortOrder).ToListAsync();
        if (id is null) return View(new SessionInput { ExamId = examId ?? 0, ExamStart = DateOnly.FromDateTime(DateTime.Today) });
        var s = await db.ExamSessions.FindAsync(id);
        if (s is null) return NotFound();
        return View(new SessionInput
        {
            Id = s.Id, ExamId = s.ExamId, Title = s.Title, RegistrationStart = s.RegistrationStart,
            RegistrationEnd = s.RegistrationEnd, ExamStart = s.ExamStart, ExamEnd = s.ExamEnd,
            IsTentative = s.IsTentative, Note = s.Note, SourceName = s.SourceName, SourceUrl = s.SourceUrl
        });
    }

    [HttpPost]
    public async Task<IActionResult> EditSession(SessionInput vm)
    {
        if (vm.ExamEnd is { } end && end < vm.ExamStart) ModelState.AddModelError(nameof(vm.ExamEnd), "結束日不可早於開始日");
        if (vm.RegistrationStart is { } rs && vm.RegistrationEnd is { } re && re < rs)
            ModelState.AddModelError(nameof(vm.RegistrationEnd), "報名截止不可早於報名開始");
        if (!await db.Exams.AnyAsync(e => e.Id == vm.ExamId)) ModelState.AddModelError(nameof(vm.ExamId), "請選擇考試");
        if (!ModelState.IsValid)
        {
            ViewBag.Exams = await db.Exams.OrderBy(e => e.SortOrder).ToListAsync();
            return View(vm);
        }
        var s = vm.Id is { } id ? await db.ExamSessions.FindAsync(id) : null;
        if (vm.Id is not null && s is null) return NotFound();
        if (s is null) db.ExamSessions.Add(s = new ExamSession());
        s.ExamId = vm.ExamId;
        s.Title = vm.Title.Trim();
        s.RegistrationStart = vm.RegistrationStart;
        s.RegistrationEnd = vm.RegistrationEnd;
        s.ExamStart = vm.ExamStart;
        s.ExamEnd = vm.ExamEnd ?? vm.ExamStart;
        s.IsTentative = vm.IsTentative;
        s.Note = vm.Note?.Trim() ?? "";
        s.SourceName = vm.SourceName.Trim();
        s.SourceUrl = vm.SourceUrl.Trim();
        await db.SaveChangesAsync();
        TempData["Flash"] = $"已儲存「{s.Title}」。";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteSession(int id)
    {
        var s = await db.ExamSessions.FindAsync(id);
        if (s is null) return NotFound();
        db.UserTargetExams.RemoveRange(db.UserTargetExams.Where(t => t.ExamSessionId == id));
        db.ExamSessions.Remove(s);
        await db.SaveChangesAsync();
        TempData["Flash"] = $"已刪除「{s.Title}」。";
        return RedirectToAction(nameof(Index));
    }
}
