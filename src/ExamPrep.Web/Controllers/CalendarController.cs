using System.Text;
using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using ExamPrep.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Controllers;

public class CalendarController(AppDbContext db, UserManager<ApplicationUser> users, CountdownService countdown) : Controller
{
    public async Task<IActionResult> Index(string? organizer)
    {
        var userId = users.GetUserId(User);
        var items = await countdown.AllAsync(userId, organizer);
        return View(new CalendarVm
        {
            Organizer = organizer,
            LoggedIn = userId is not null,
            Upcoming = items.Where(i => !countdown.IsPast(i.Session)).ToList(),
            Past = items.Where(i => countdown.IsPast(i.Session)).OrderByDescending(i => i.Session.ExamStart).ToList()
        });
    }

    [HttpPost, Authorize]
    public async Task<IActionResult> ToggleTarget(int id, string? returnUrl)
    {
        var userId = users.GetUserId(User)!;
        if (!await db.ExamSessions.AnyAsync(s => s.Id == id)) return NotFound();
        var existing = await db.UserTargetExams.FirstOrDefaultAsync(t => t.UserId == userId && t.ExamSessionId == id);
        if (existing is null) db.UserTargetExams.Add(new UserTargetExam { UserId = userId, ExamSessionId = id });
        else db.UserTargetExams.Remove(existing);
        await db.SaveChangesAsync();
        return Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl!) : RedirectToAction(nameof(Index));
    }

    /// <summary>匯出 iCalendar，可加入 Google 日曆／Outlook。登入時只匯出「我的目標」，否則匯出全部未來考期。</summary>
    public async Task<IActionResult> Ics()
    {
        var userId = users.GetUserId(User);
        var items = (await countdown.AllAsync(userId)).Where(i => !countdown.IsPast(i.Session)).ToList();
        if (userId is not null && items.Any(i => i.IsTarget)) items = items.Where(i => i.IsTarget).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("BEGIN:VCALENDAR").AppendLine("VERSION:2.0").AppendLine("PRODID:-//ExamPrep//ZH-TW").AppendLine("CALSCALE:GREGORIAN");
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        foreach (var i in items)
        {
            var s = i.Session;
            AddEvent(sb, $"exam-{s.Id}@examprep", stamp, s.ExamStart, s.ExamEnd, $"【考試】{s.Title}",
                $"{(s.IsTentative ? "日期暫定，" : "")}來源：{s.SourceName} {s.SourceUrl}");
            if (s.RegistrationStart is { } rs && s.RegistrationEnd is { } re)
                AddEvent(sb, $"reg-{s.Id}@examprep", stamp, rs, re, $"【報名】{s.Title}", $"來源：{s.SourceName} {s.SourceUrl}");
        }
        sb.AppendLine("END:VCALENDAR");
        return File(Encoding.UTF8.GetBytes(sb.ToString().Replace("\n", "\r\n").Replace("\r\r\n", "\r\n")), "text/calendar", "exam-calendar.ics");
    }

    private static void AddEvent(StringBuilder sb, string uid, string stamp, DateOnly start, DateOnly end, string summary, string desc)
    {
        sb.AppendLine("BEGIN:VEVENT")
          .AppendLine($"UID:{uid}")
          .AppendLine($"DTSTAMP:{stamp}")
          .AppendLine($"DTSTART;VALUE=DATE:{start:yyyyMMdd}")
          .AppendLine($"DTEND;VALUE=DATE:{end.AddDays(1):yyyyMMdd}")
          .AppendLine($"SUMMARY:{Escape(summary)}")
          .AppendLine($"DESCRIPTION:{Escape(desc)}")
          .AppendLine("END:VEVENT");
    }

    private static string Escape(string s) => s.Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,").Replace("\n", "\\n");
}
