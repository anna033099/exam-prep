using System.Diagnostics;
using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using ExamPrep.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Controllers;

public class HomeController(AppDbContext db, UserManager<ApplicationUser> users, CountdownService countdown, TaipeiDate date) : Controller
{
    public async Task<IActionResult> Index()
    {
        var userId = users.GetUserId(User);
        if (userId is null)
        {
            var all = await countdown.AllAsync(null);
            var exams = await db.Exams.OrderBy(e => e.SortOrder).AsNoTracking().ToListAsync();
            var stats = await db.Papers.Where(p => p.ExamId != null)
                .GroupBy(p => p.ExamId!.Value)
                .Select(g => new { g.Key, Papers = g.Count(), Questions = g.Sum(p => p.Questions.Count), First = g.Min(p => p.RocYear), Last = g.Max(p => p.RocYear) })
                .ToDictionaryAsync(x => x.Key);
            // 首頁示範題：隨機挑一題題幹短、無題組文章的單選題
            var candidates = db.Questions.Where(q => q.Type == QuestionType.Single && q.Passage == null && q.Stem.Length < 70
                                                      && q.OptionA.Length < 30 && q.OptionB.Length < 30 && q.OptionC.Length < 30 && q.OptionD.Length < 30);
            var n = await candidates.CountAsync();
            var sample = n == 0 ? null : await candidates.Include(q => q.Paper).OrderBy(q => q.Id).Skip(Random.Shared.Next(n)).FirstOrDefaultAsync();
            return View("Landing", new LandingVm
            {
                ExamStats = exams.Select(e => stats.TryGetValue(e.Id, out var x)
                    ? new ExamStat(e, x.Papers, x.Questions, x.First, x.Last) : new ExamStat(e, 0, 0, 0, 0)).ToList(),
                Sample = sample,
                PaperCount = await db.Papers.CountAsync(),
                QuestionCount = await db.Questions.CountAsync(),
                ExamCount = await db.Exams.CountAsync(),
                Upcoming = all.Where(i => !countdown.IsPast(i.Session)).Take(4).ToList()
            });
        }

        var user = await users.GetUserAsync(User);
        var items = await countdown.AllAsync(userId);
        var upcoming = items.Where(i => !countdown.IsPast(i.Session)).ToList();
        var today = date.Today;
        var weekStart = today.AddDays(-6);

        var logs = await db.StudyLogs.Where(l => l.UserId == userId && l.Date >= today.AddDays(-60))
            .Select(l => new { l.Date, l.Minutes }).ToListAsync();
        var byDay = logs.GroupBy(l => l.Date).ToDictionary(g => g.Key, g => g.Sum(x => x.Minutes));
        int streak = 0;
        for (var d = byDay.ContainsKey(today) ? today : today.AddDays(-1); byDay.ContainsKey(d); d = d.AddDays(-1)) streak++;

        var answers = db.PracticeAnswers.Where(a => a.Session!.UserId == userId);
        var answered = await answers.CountAsync();
        var correct = await answers.CountAsync(a => a.IsCorrect);
        var bySubject = await answers
            .GroupBy(a => a.Question!.Paper!.SubjectName)
            .Select(g => new SubjectAccuracy(g.Key, g.Count(), g.Count(a => a.IsCorrect)))
            .ToListAsync();

        var vm = new DashboardVm
        {
            DisplayName = user?.DisplayName ?? "",
            Targets = upcoming.Where(i => i.IsTarget).ToList(),
            NextExam = upcoming.FirstOrDefault(),
            TodayMinutes = byDay.GetValueOrDefault(today),
            WeekMinutes = byDay.Where(kv => kv.Key >= weekStart).Sum(kv => kv.Value),
            DailyGoalMinutes = (await db.UserSettings.FindAsync(userId))?.DailyGoalMinutes ?? 180,
            StreakDays = streak,
            AnsweredCount = answered,
            Accuracy = answered == 0 ? null : (double)correct / answered,
            OpenWrongCount = await db.WrongQuestions.CountAsync(w => w.UserId == userId && !w.Mastered),
            WeakSubjects = bySubject.Where(s => s.Answered >= 10).OrderBy(s => s.Rate).Take(5).ToList(),
            RecentSessions = await db.PracticeSessions.Where(s => s.UserId == userId && s.FinishedAt != null)
                .OrderByDescending(s => s.FinishedAt).Take(5).ToListAsync()
        };
        return View(vm);
    }

    public IActionResult About() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(model: Activity.Current?.Id ?? HttpContext.TraceIdentifier);
}
