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
            return View("Landing", new LandingVm
            {
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
