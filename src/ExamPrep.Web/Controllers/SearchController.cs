using ExamPrep.Web.Data;
using ExamPrep.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ExamPrep.Web.Controllers;

/// <summary>題目全文搜尋：題幹、選項、題組文章。</summary>
public class SearchController(AppDbContext db, IMemoryCache cache) : Controller
{
    /// <summary>最多列出的結果數；題庫有數十萬題，太泛的關鍵字只列最新的這些。</summary>
    public const int MaxResults = 2000;

    public async Task<IActionResult> Index(string? q, string? exam, int page = 1)
    {
        var vm = new SearchVm { Q = q?.Trim(), Exam = exam, Exams = await db.Exams.OrderBy(e => e.SortOrder).ToListAsync() };
        if (string.IsNullOrWhiteSpace(vm.Q) || vm.Q.Length < 2) return View(vm);
        var term = (vm.Q.Length > 50 ? vm.Q[..50] : vm.Q).ToUpperInvariant();

        // 掃一次資料表取得符合的題目編號（依年度新到舊），快取 10 分鐘供翻頁使用
        var ids = await cache.GetOrCreateAsync($"search:{exam}:{term}", async e =>
        {
            e.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var query = db.Questions.Where(x => !x.Paper!.ContentWithheld && x.SearchText.Contains(term));
            if (!string.IsNullOrEmpty(exam)) query = query.Where(x => x.Paper!.ExamLinks.Any(l => l.Exam!.Slug == exam));
            return await query.OrderByDescending(x => x.Paper!.RocYear).ThenBy(x => x.PaperId).ThenBy(x => x.Number)
                .Select(x => x.Id).Take(MaxResults + 1).ToListAsync();
        }) ?? new();

        vm.Truncated = ids.Count > MaxResults;
        vm.Total = Math.Min(ids.Count, MaxResults);
        vm.PageCount = Math.Max(1, (int)Math.Ceiling(vm.Total / (double)SearchVm.PageSize));
        vm.Page = Math.Clamp(page, 1, vm.PageCount);
        var pageIds = ids.Take(MaxResults).Skip((vm.Page - 1) * SearchVm.PageSize).Take(SearchVm.PageSize).ToList();
        var rows = await db.Questions.Include(x => x.Paper).Where(x => pageIds.Contains(x.Id)).ToListAsync();
        vm.Results = pageIds.Select(id => rows.First(r => r.Id == id)).ToList();
        return View(vm);
    }
}
