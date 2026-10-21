using ExamPrep.Web.Data;
using ExamPrep.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Controllers;

/// <summary>題目全文搜尋：題幹、選項、題組文章。</summary>
public class SearchController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? q, string? exam, int page = 1)
    {
        var vm = new SearchVm { Q = q?.Trim(), Exam = exam, Exams = await db.Exams.OrderBy(e => e.SortOrder).ToListAsync() };
        if (string.IsNullOrWhiteSpace(vm.Q) || vm.Q.Length < 2) return View(vm);
        var term = vm.Q.Length > 50 ? vm.Q[..50] : vm.Q;
        var query = db.Questions.Where(x => !x.Paper!.ContentWithheld)
            .Where(x => x.Stem.Contains(term) || x.OptionA.Contains(term) || x.OptionB.Contains(term) || x.OptionC.Contains(term)
                        || x.OptionD.Contains(term) || (x.OptionE != null && x.OptionE.Contains(term)) || (x.Passage != null && x.Passage.Contains(term)));
        if (!string.IsNullOrEmpty(exam)) query = query.Where(x => x.Paper!.Exam!.Slug == exam);
        vm.Total = await query.CountAsync();
        vm.PageCount = Math.Max(1, (int)Math.Ceiling(vm.Total / (double)SearchVm.PageSize));
        vm.Page = Math.Clamp(page, 1, vm.PageCount);
        vm.Results = await query.Include(x => x.Paper).OrderByDescending(x => x.Paper!.RocYear).ThenBy(x => x.PaperId).ThenBy(x => x.Number)
            .Skip((vm.Page - 1) * SearchVm.PageSize).Take(SearchVm.PageSize).ToListAsync();
        return View(vm);
    }
}
