using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace ExamPrep.Web.Services;

/// <summary>
/// 題庫目錄的彙總數字（各考試試卷數、各科題數、年度清單）。題庫只在啟動匯入時變動，
/// 這些查詢要掃過數十萬題，所以快取起來；匯入完成後呼叫 <see cref="Clear"/>。
/// </summary>
public class CatalogCache(IServiceScopeFactory scopes, IMemoryCache cache)
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);
    private static CancellationTokenSource _reset = new();

    public static void Clear()
    {
        var old = Interlocked.Exchange(ref _reset, new CancellationTokenSource());
        old.Cancel();
        old.Dispose();
    }

    private Task<T> GetAsync<T>(string key, Func<AppDbContext, Task<T>> load) =>
        cache.GetOrCreateAsync(key, async e =>
        {
            e.AbsoluteExpirationRelativeToNow = Ttl;
            e.AddExpirationToken(new CancellationChangeToken(_reset.Token));
            using var scope = scopes.CreateScope();
            return await load(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        })!;

    public Task<List<Exam>> ExamsAsync() =>
        GetAsync("exams", db => db.Exams.AsNoTracking().OrderBy(e => e.SortOrder).ToListAsync());

    /// <summary>各考試類別的試卷數（含共用試卷）。</summary>
    public Task<Dictionary<string, int>> ExamPaperCountsAsync() =>
        GetAsync("exam-papers", db => db.PaperExamLinks.GroupBy(l => l.Exam!.Slug)
            .Select(g => new { g.Key, C = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.C));

    /// <summary>各考試類別可作答的選擇題數與申論題數。</summary>
    public Task<Dictionary<string, (int Choice, int Essay)>> ExamQuestionCountsAsync() =>
        GetAsync("exam-questions", async db =>
        {
            var rows = await db.PaperExamLinks
                .Select(l => new { l.Exam!.Slug, l.PaperId })
                .Join(db.Questions, l => l.PaperId, q => q.PaperId, (l, q) => new { l.Slug, Essay = q.Type == QuestionType.Essay })
                .GroupBy(x => new { x.Slug, x.Essay })
                .Select(g => new { g.Key.Slug, g.Key.Essay, C = g.Count() }).ToListAsync();
            return rows.GroupBy(r => r.Slug).ToDictionary(g => g.Key,
                g => (g.Where(r => !r.Essay).Sum(r => r.C), g.Where(r => r.Essay).Sum(r => r.C)));
        });

    /// <summary>各考試類別：試卷數、選擇題數、申論題數、最早與最晚年度。</summary>
    public Task<Dictionary<int, (int Papers, int Choice, int Essay, int First, int Last)>> ExamStatsAsync() =>
        GetAsync("exam-stats", async db =>
        {
            var papers = await db.PaperExamLinks.GroupBy(l => l.ExamId)
                .Select(g => new { g.Key, N = g.Count(), First = g.Min(l => l.Paper!.RocYear), Last = g.Max(l => l.Paper!.RocYear) })
                .ToListAsync();
            var qs = await db.PaperExamLinks
                .Join(db.Questions, l => l.PaperId, q => q.PaperId, (l, q) => new { l.ExamId, Essay = q.Type == QuestionType.Essay })
                .GroupBy(x => new { x.ExamId, x.Essay }).Select(g => new { g.Key.ExamId, g.Key.Essay, C = g.Count() }).ToListAsync();
            return papers.ToDictionary(p => p.Key, p => (p.N,
                qs.Where(q => q.ExamId == p.Key && !q.Essay).Sum(q => q.C),
                qs.Where(q => q.ExamId == p.Key && q.Essay).Sum(q => q.C), p.First, p.Last));
        });

    /// <summary>首頁「試試看」的候選題目編號（題幹短、無題組文章的單選題），最多 3000 題。</summary>
    public Task<List<int>> SampleQuestionIdsAsync() =>
        GetAsync("sample-ids", db => db.Questions.Where(q => q.Type == QuestionType.Single && q.Passage == null && q.Stem.Length < 70
                    && q.OptionA.Length < 30 && q.OptionB.Length < 30 && q.OptionC.Length < 30 && q.OptionD.Length < 30 && !q.Paper!.ContentWithheld)
                .OrderBy(q => q.Id).Select(q => q.Id).Take(3000).ToListAsync());

    public Task<int> AllPaperCountAsync() => GetAsync("all-papers", db => db.Papers.CountAsync());

    public Task<(int Choice, int Essay)> AllQuestionCountsAsync() =>
        GetAsync("all-questions", async db =>
            (await db.Questions.CountAsync(q => q.Type != QuestionType.Essay), await db.Questions.CountAsync(q => q.Type == QuestionType.Essay)));

    public Task<List<int>> YearsAsync() =>
        GetAsync("years", db => db.Papers.Select(p => p.RocYear).Distinct().OrderByDescending(y => y).ToListAsync());

    /// <summary>可作答選擇題的科目與題數（公開版不提供內文的試卷不算）。</summary>
    public Task<List<(string Subject, int Count)>> SubjectCountsAsync(string? examSlug) =>
        GetAsync("subjects:" + (examSlug ?? ""), async db =>
        {
            var q = db.Questions.Where(x => !x.Paper!.ContentWithheld && x.Type != QuestionType.Essay);
            if (!string.IsNullOrEmpty(examSlug)) q = q.Where(x => x.Paper!.ExamLinks.Any(l => l.Exam!.Slug == examSlug));
            var rows = await q.GroupBy(x => x.Paper!.SubjectName).Select(g => new { g.Key, C = g.Count() })
                .OrderByDescending(x => x.C).ToListAsync();
            return rows.Select(r => (r.Key, r.C)).ToList();
        });
}
