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
        var mine = db.WrongQuestions.Where(w => w.UserId == UserId);
        var subjects = await mine.Where(w => !w.Mastered)
            .GroupBy(w => w.Question!.Paper!.SubjectName)
            .Select(g => new { g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).ToListAsync();

        var q = mine.Include(w => w.Question).ThenInclude(x => x!.Paper).AsQueryable();
        if (!showMastered) q = q.Where(w => !w.Mastered);
        if (!string.IsNullOrEmpty(subject)) q = q.Where(w => w.Question!.Paper!.SubjectName == subject);

        return View(new WrongBookVm
        {
            Subject = subject,
            ShowMastered = showMastered,
            Subjects = subjects.Select(s => (s.Key, s.Count)).ToList(),
            Items = await q.OrderByDescending(w => w.WrongCount).ThenByDescending(w => w.LastWrongAt).Take(200).ToListAsync()
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
        w.Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 1000)];
        await db.SaveChangesAsync();
        TempData["Flash"] = "筆記已儲存。";
        return Redirect(Url.Action(nameof(Index), new { subject }) + $"#w{id}");
    }
}
