using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Controllers;

public class ExamsController(AppDbContext db, UserManager<ApplicationUser> users, CountdownService countdown) : Controller
{
    public async Task<IActionResult> Index()
    {
        var exams = await db.Exams.Include(e => e.Sessions).OrderBy(e => e.SortOrder).AsNoTracking().ToListAsync();
        ViewBag.PaperCounts = await db.Papers.Where(p => p.ExamId != null)
            .GroupBy(p => p.ExamId!.Value).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        return View(exams);
    }

    [Route("Exams/{slug}")]
    public async Task<IActionResult> Detail(string slug)
    {
        var exam = await db.Exams.Include(e => e.Sessions).Include(e => e.Subjects)
            .AsNoTracking().FirstOrDefaultAsync(e => e.Slug == slug);
        if (exam is null) return NotFound();
        var userId = users.GetUserId(User);
        var targets = userId is null ? new HashSet<int>()
            : (await db.UserTargetExams.Where(t => t.UserId == userId).Select(t => t.ExamSessionId).ToListAsync()).ToHashSet();
        ViewBag.Sessions = exam.Sessions.OrderByDescending(s => s.ExamStart).Select(s => countdown.ToItem(s, targets.Contains(s.Id))).ToList();
        ViewBag.Papers = await db.Papers.Where(p => p.ExamId == exam.Id)
            .OrderByDescending(p => p.RocYear).ThenBy(p => p.SubjectName)
            .Select(p => new ViewModels.PaperRow { Id = p.Id, RocYear = p.RocYear, SubjectName = p.SubjectName, QuestionCount = p.Questions.Count, OfficialCount = p.OfficialQuestionCount })
            .ToListAsync();
        return View(exam);
    }
}
