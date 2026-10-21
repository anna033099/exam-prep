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
public class StudyController(AppDbContext db, UserManager<ApplicationUser> users, TaipeiDate date) : Controller
{
    private string UserId => users.GetUserId(User)!;

    public async Task<IActionResult> Index()
    {
        var today = date.Today;
        var subjects = await db.StudySubjects.Where(s => s.UserId == UserId && !s.Archived).OrderBy(s => s.Id).ToListAsync();
        var totals = await db.StudyLogs.Where(l => l.UserId == UserId)
            .GroupBy(l => l.StudySubjectId)
            .Select(g => new { g.Key, Total = g.Sum(x => x.Minutes), Last7 = g.Where(x => x.Date > today.AddDays(-7)).Sum(x => x.Minutes) })
            .ToDictionaryAsync(x => x.Key);
        var since = today.AddDays(-13);
        var recent = await db.StudyLogs.Include(l => l.StudySubject)
            .Where(l => l.UserId == UserId && l.Date >= since).ToListAsync();

        var days = Enumerable.Range(0, 14).Select(i => since.AddDays(i)).Select(d =>
        {
            var dayLogs = recent.Where(l => l.Date == d).ToList();
            var parts = dayLogs.GroupBy(l => l.StudySubject!.Color).Select(g => (g.Key, g.Sum(x => x.Minutes))).ToList();
            return new DayTotal(d, dayLogs.Sum(l => l.Minutes), parts);
        }).ToList();

        return View(new StudyVm
        {
            Today = today,
            Subjects = subjects.Select(s => new StudySubjectProgress(s,
                totals.TryGetValue(s.Id, out var t) ? t.Total : 0,
                totals.TryGetValue(s.Id, out var t2) ? t2.Last7 : 0)).ToList(),
            Last14Days = days,
            RecentLogs = await db.StudyLogs.Include(l => l.StudySubject).Where(l => l.UserId == UserId)
                .OrderByDescending(l => l.Date).ThenByDescending(l => l.Id).Take(20).ToListAsync(),
            DailyGoalMinutes = (await db.UserSettings.FindAsync(UserId))?.DailyGoalMinutes ?? 180,
            NewLog = new LogInput { Date = today }
        });
    }

    [HttpPost]
    public async Task<IActionResult> SaveSubject(SubjectInput input)
    {
        if (!ModelState.IsValid)
        {
            TempData["Flash"] = string.Join(" ", ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return RedirectToAction(nameof(Index));
        }
        StudySubject? s;
        if (input.Id is { } id)
        {
            s = await db.StudySubjects.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
            if (s is null) return NotFound();
        }
        else
        {
            s = new StudySubject { UserId = UserId };
            db.StudySubjects.Add(s);
        }
        s.Name = input.Name.Trim();
        s.TargetHours = input.TargetHours;
        s.Color = input.Color;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> ArchiveSubject(int id)
    {
        var s = await db.StudySubjects.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (s is null) return NotFound();
        s.Archived = true;
        await db.SaveChangesAsync();
        TempData["Flash"] = $"已封存「{s.Name}」，歷史紀錄仍保留。";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>依考試科目一鍵建立讀書科目（只建立尚未存在的）。</summary>
    [HttpPost]
    public async Task<IActionResult> ImportSubjects(int examId, string track)
    {
        var names = await db.ExamSubjects.Where(s => s.ExamId == examId && s.Track == track)
            .OrderBy(s => s.SortOrder).Select(s => s.SubjectName).ToListAsync();
        var existing = (await db.StudySubjects.Where(s => s.UserId == UserId && !s.Archived).Select(s => s.Name).ToListAsync()).ToHashSet();
        string[] palette = ["#2f6f5e", "#b5563a", "#3b5b92", "#8a6d1f", "#6b4c8a", "#2d7d8c", "#9a3b5c"];
        int added = 0, i = existing.Count;
        foreach (var n in names.Where(n => !existing.Contains(n)))
        {
            db.StudySubjects.Add(new StudySubject { UserId = UserId, Name = n, TargetHours = 60, Color = palette[i++ % palette.Length] });
            added++;
        }
        await db.SaveChangesAsync();
        TempData["Flash"] = added > 0 ? $"已加入 {added} 個科目，目標時數預設 60 小時，可再調整。" : "這些科目都已在你的讀書計畫中。";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> Log([Bind(Prefix = "NewLog")] LogInput input)
    {
        if (!ModelState.IsValid || !await db.StudySubjects.AnyAsync(s => s.Id == input.StudySubjectId && s.UserId == UserId))
        {
            TempData["Flash"] = "紀錄未儲存：請確認科目與分鐘數。";
            return RedirectToAction(nameof(Index));
        }
        if (input.Date > date.Today)
        {
            TempData["Flash"] = "不能記錄未來日期的讀書時間。";
            return RedirectToAction(nameof(Index));
        }
        db.StudyLogs.Add(new StudyLog
        {
            UserId = UserId, StudySubjectId = input.StudySubjectId!.Value, Date = input.Date,
            Minutes = input.Minutes, Note = string.IsNullOrWhiteSpace(input.Note) ? null : input.Note.Trim()
        });
        await db.SaveChangesAsync();
        TempData["Flash"] = $"已記錄 {input.Minutes} 分鐘。";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> DeleteLog(int id)
    {
        var l = await db.StudyLogs.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (l is null) return NotFound();
        db.StudyLogs.Remove(l);
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public async Task<IActionResult> SetGoal(int minutes)
    {
        minutes = Math.Clamp(minutes, 10, 1200);
        var s = await db.UserSettings.FindAsync(UserId);
        if (s is null) db.UserSettings.Add(new UserSetting { UserId = UserId, DailyGoalMinutes = minutes });
        else s.DailyGoalMinutes = minutes;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}
