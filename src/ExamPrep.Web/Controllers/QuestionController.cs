using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using ExamPrep.Web.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Controllers;

/// <summary>單題頁：看答案、收藏、個人筆記、討論、勘誤回報、AI 詳解。</summary>
[Route("Q")]
public class QuestionController(AppDbContext db, UserManager<ApplicationUser> users, AiExplanationService ai, TimeProvider clock) : Controller
{
    public const int CommentMaxLength = 1000;
    public const int HideAfterReports = 3;

    private string? UserId => users.GetUserId(User);

    private Task<Question?> FindAsync(int id) =>
        db.Questions.Include(q => q.Paper).ThenInclude(p => p!.Exam)
            .FirstOrDefaultAsync(q => q.Id == id && !q.Paper!.ContentWithheld);

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        var q = await FindAsync(id);
        if (q is null) return NotFound();
        var uid = UserId;
        var vm = new QuestionPageVm
        {
            Question = q,
            SignedIn = uid is not null,
            AiEnabled = ai.Enabled,
            Explanation = await ai.GetCachedAsync(id),
            Comments = await db.Comments.Include(c => c.User).Where(c => c.QuestionId == id && !c.IsHidden)
                .OrderBy(c => c.CreatedAt).Take(200).ToListAsync(),
            OpenErrata = await db.ErrataReports.CountAsync(e => e.QuestionId == id && e.Status == ErrataStatus.Open)
        };
        if (uid is not null)
        {
            vm.Bookmarked = await db.Bookmarks.AnyAsync(b => b.UserId == uid && b.QuestionId == id);
            vm.Note = await db.QuestionNotes.Where(n => n.UserId == uid && n.QuestionId == id).Select(n => n.Body).FirstOrDefaultAsync();
            vm.LastAnswer = await db.PracticeAnswers.Where(a => a.Session!.UserId == uid && a.QuestionId == id)
                .OrderByDescending(a => a.AnsweredAt).FirstOrDefaultAsync();
        }
        return View(vm);
    }

    [Authorize, HttpPost("{id:int}/bookmark")]
    public async Task<IActionResult> Bookmark(int id, string? returnUrl)
    {
        if (await FindAsync(id) is null) return NotFound();
        var b = await db.Bookmarks.FirstOrDefaultAsync(x => x.UserId == UserId && x.QuestionId == id);
        if (b is null) db.Bookmarks.Add(new QuestionBookmark { UserId = UserId!, QuestionId = id, CreatedAt = clock.GetUtcNow().UtcDateTime });
        else db.Bookmarks.Remove(b);
        await db.SaveChangesAsync();
        if (Request.Headers.Accept.ToString().Contains("application/json")) return Json(new { bookmarked = b is null });
        return Back(returnUrl, id);
    }

    [Authorize, HttpPost("{id:int}/note")]
    public async Task<IActionResult> Note(int id, string? body, string? returnUrl)
    {
        if (await FindAsync(id) is null) return NotFound();
        var n = await db.QuestionNotes.FirstOrDefaultAsync(x => x.UserId == UserId && x.QuestionId == id);
        var text = (body ?? "").Trim();
        if (text.Length > 2000) text = text[..2000];
        if (text.Length == 0)
        {
            if (n is not null) db.QuestionNotes.Remove(n);
        }
        else
        {
            if (n is null) db.QuestionNotes.Add(n = new QuestionNote { UserId = UserId!, QuestionId = id });
            n.Body = text;
            n.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        }
        await db.SaveChangesAsync();
        TempData["Flash"] = text.Length == 0 ? "筆記已刪除。" : "筆記已儲存。";
        return Back(returnUrl, id);
    }

    [Authorize, HttpPost("{id:int}/comment")]
    public async Task<IActionResult> Comment(int id, string? body)
    {
        if (await FindAsync(id) is null) return NotFound();
        var text = (body ?? "").Trim();
        if (text.Length is 0 or > CommentMaxLength)
        {
            TempData["Flash"] = $"留言需介於 1～{CommentMaxLength} 字。";
            return Redirect(Url.Action(nameof(Detail), new { id }) + "#discuss");
        }
        var since = clock.GetUtcNow().UtcDateTime.AddSeconds(-20);
        if (await db.Comments.AnyAsync(c => c.UserId == UserId && c.CreatedAt >= since))
        {
            TempData["Flash"] = "留言太頻繁，請稍等 20 秒再送出。";
            return Redirect(Url.Action(nameof(Detail), new { id }) + "#discuss");
        }
        db.Comments.Add(new QuestionComment { QuestionId = id, UserId = UserId!, Body = text, CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync();
        return Redirect(Url.Action(nameof(Detail), new { id }) + "#discuss");
    }

    [Authorize, HttpPost("comment/{commentId:int}/report")]
    public async Task<IActionResult> ReportComment(int commentId)
    {
        var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == commentId);
        if (c is null) return NotFound();
        c.ReportCount++;
        if (c.ReportCount >= HideAfterReports) c.IsHidden = true;
        await db.SaveChangesAsync();
        TempData["Flash"] = "已檢舉，管理員會處理。";
        return Redirect(Url.Action(nameof(Detail), new { id = c.QuestionId }) + "#discuss");
    }

    [Authorize, HttpPost("comment/{commentId:int}/delete")]
    public async Task<IActionResult> DeleteComment(int commentId)
    {
        var c = await db.Comments.FirstOrDefaultAsync(x => x.Id == commentId);
        if (c is null) return NotFound();
        if (c.UserId != UserId && !User.IsInRole(AppInitializer.AdminRole)) return Forbid();
        db.Comments.Remove(c);
        await db.SaveChangesAsync();
        return Redirect(Url.Action(nameof(Detail), new { id = c.QuestionId }) + "#discuss");
    }

    [Authorize, HttpPost("{id:int}/errata")]
    public async Task<IActionResult> Errata(int id, ErrataKind kind, string? detail)
    {
        if (await FindAsync(id) is null) return NotFound();
        var text = (detail ?? "").Trim();
        if (text.Length is 0 or > 1000)
        {
            TempData["Flash"] = "請說明哪裡有誤（1～1000 字）。";
            return Redirect(Url.Action(nameof(Detail), new { id }) + "#errata");
        }
        db.ErrataReports.Add(new ErrataReport { QuestionId = id, UserId = UserId!, Kind = kind, Detail = text, CreatedAt = clock.GetUtcNow().UtcDateTime });
        await db.SaveChangesAsync();
        TempData["Flash"] = "已收到回報，謝謝。管理員會對照原卷確認。";
        return Redirect(Url.Action(nameof(Detail), new { id }) + "#errata");
    }

    [Authorize, HttpPost("{id:int}/explain")]
    public async Task<IActionResult> Explain(int id, CancellationToken ct)
    {
        var r = await ai.GetOrCreateAsync(id, UserId!, ct);
        TempData["Flash"] = r.Status switch
        {
            AiResultStatus.Disabled => "此網站未啟用 AI 詳解。",
            AiResultStatus.LimitReached => "今天產生 AI 詳解的次數已用完，明天再試。",
            AiResultStatus.Failed => r.Error ?? "AI 詳解產生失敗。",
            _ => null
        };
        return Redirect(Url.Action(nameof(Detail), new { id }) + "#ai");
    }

    private IActionResult Back(string? returnUrl, int id) =>
        !string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? LocalRedirect(returnUrl) : RedirectToAction(nameof(Detail), new { id });
}
