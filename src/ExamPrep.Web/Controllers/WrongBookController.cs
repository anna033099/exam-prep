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
public class WrongBookController(AppDbContext db, UserManager<ApplicationUser> users, PracticeService practice) : Controller
{
    private string UserId => users.GetUserId(User)!;

    public async Task<IActionResult> Index(string? subject, bool showMastered = false)
    {
        var mine = db.WrongQuestions.Where(w => w.UserId == UserId && !w.Question!.Paper!.ContentWithheld);
        var subjects = await mine.Where(w => !w.Mastered)
            .GroupBy(w => w.Question!.Paper!.SubjectName)
            .Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync();

        var q = mine.Include(w => w.Question).ThenInclude(x => x!.Paper).AsQueryable();
        if (!showMastered) q = q.Where(w => !w.Mastered);
        if (!string.IsNullOrEmpty(subject)) q = q.Where(w => w.Question!.Paper!.SubjectName == subject);

        var items = await q.OrderByDescending(w => w.WrongCount).ThenByDescending(w => w.LastWrongAt).Take(200).ToListAsync();
        var qids = items.Select(w => w.QuestionId).ToList();
        return View(new WrongBookVm
        {
            DueCount = await practice.DueQuery(UserId).CountAsync(),
            Notes = await db.QuestionNotes.Where(n => n.UserId == UserId && qids.Contains(n.QuestionId)).ToDictionaryAsync(n => n.QuestionId, n => n.Body),
            Subject = subject,
            ShowMastered = showMastered,
            Subjects = subjects.Select(s => (s.Key, s.Count)).ToList(),
            Items = items
        });
    }

    [HttpPost]
    public async Task<IActionResult> Retry(string? subject, int count = 20)
    {
        var s = await practice.CreateFromWrongBookAsync(UserId, subject, count);
        if (s is null)
        {
            TempData["Flash"] = "目前沒有待複習的錯題。";
            return RedirectToAction(nameof(Index));
        }
        return RedirectToAction("Take", "Practice", new { id = s.Id });
    }

    [HttpPost]
    public async Task<IActionResult> ToggleMastered(int id, string? subject)
    {
        var w = await db.WrongQuestions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (w is null) return NotFound();
        w.Mastered = !w.Mastered;
        w.CorrectStreak = 0;
        await db.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { subject });
    }

    [HttpPost]
    public async Task<IActionResult> SaveNote(int id, string? note, string? subject)
    {
        var w = await db.WrongQuestions.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (w is null) return NotFound();
        // 錯題筆記與單題頁共用同一份個人筆記
        var text = (note ?? "").Trim();
        if (text.Length > 2000) text = text[..2000];
        var n = await db.QuestionNotes.FirstOrDefaultAsync(x => x.UserId == UserId && x.QuestionId == w.QuestionId);
        if (text.Length == 0) { if (n is not null) db.QuestionNotes.Remove(n); }
        else
        {
            if (n is null) db.QuestionNotes.Add(n = new QuestionNote { UserId = UserId, QuestionId = w.QuestionId });
            n.Body = text;
            n.UpdatedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        TempData["Flash"] = "筆記已儲存。";
        return Redirect(Url.Action(nameof(Index), new { subject }) + $"#w{id}");
    }
}
