using System.Diagnostics;
using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using ExamPrep.Web.ViewModels;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Controllers;

public class HomeController(AppDbContext db, UserManager<ApplicationUser> users, CountdownService countdown, TaipeiDate date, StatsService stats, CatalogCache catalog) : Controller
{
    public async Task<IActionResult> Index()
    {
        var userId = users.GetUserId(User);
        if (userId is null)
        {
            var all = await countdown.AllAsync(null);
            var exams = await catalog.ExamsAsync();
            var examStats = await catalog.ExamStatsAsync();
            // 首頁示範題：從快取的候選題目中隨機挑一題
            var ids = await catalog.SampleQuestionIdsAsync();
            var pickId = ids.Count == 0 ? 0 : ids[Random.Shared.Next(ids.Count)];
            var sample = ids.Count == 0 ? null : await db.Questions.Include(q => q.Paper).AsNoTracking().FirstOrDefaultAsync(q => q.Id == pickId);
            var (choice, essay) = await catalog.AllQuestionCountsAsync();
            return View("Landing", new LandingVm
            {
                ExamStats = exams.Select(e => examStats.TryGetValue(e.Id, out var x)
                    ? new ExamStat(e, x.Papers, x.Choice, x.First, x.Last, x.Essay) : new ExamStat(e, 0, 0, 0, 0)).ToList(),
                Sample = sample,
                PaperCount = await catalog.AllPaperCountAsync(),
                QuestionCount = choice,
                EssayCount = essay,
                ExamCount = exams.Count,
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
        vm.Daily = await stats.DailyAsync(userId);
        return View(vm);
    }

    [Microsoft.AspNetCore.Authorization.Authorize, HttpPost]
    public async Task<IActionResult> SetQuestionGoal(int count)
    {
        var uid = users.GetUserId(User)!;
        var s = await db.UserSettings.FindAsync(uid);
        if (s is null) db.UserSettings.Add(s = new UserSetting { UserId = uid });
        s.DailyGoalQuestions = Math.Clamp(count, 5, 100);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public IActionResult About() => View();

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error() => View(model: Activity.Current?.Id ?? HttpContext.TraceIdentifier);
}
