using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Controllers;

public class ExamsController(AppDbContext db, UserManager<ApplicationUser> users, CountdownService countdown, CatalogCache catalog) : Controller
{
    public async Task<IActionResult> Index()
    {
        var exams = await db.Exams.Include(e => e.Sessions).OrderBy(e => e.SortOrder).AsNoTracking().ToListAsync();
        ViewBag.PaperCounts = (await catalog.ExamStatsAsync()).ToDictionary(x => x.Key, x => x.Value.Papers);
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
        ViewBag.Papers = await db.Papers.Where(p => p.ExamLinks.Any(l => l.ExamId == exam.Id))
            .OrderByDescending(p => p.RocYear).ThenBy(p => p.SubjectName).Take(12)
            .Select(p => new ViewModels.PaperRow { Id = p.Id, RocYear = p.RocYear, SubjectName = p.SubjectName, QuestionCount = p.Questions.Count(q => q.Type != QuestionType.Essay), EssayCount = p.Questions.Count(q => q.Type == QuestionType.Essay), OfficialCount = p.OfficialQuestionCount })
            .ToListAsync();
        ViewBag.PaperTotal = (await catalog.ExamStatsAsync()).TryGetValue(exam.Id, out var st) ? st.Papers : 0;
        return View(exam);
    }
}
