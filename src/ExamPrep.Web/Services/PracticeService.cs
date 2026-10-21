using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Services;

/// <summary>自訂出題的條件。</summary>
public record CustomQuery(
    string? ExamSlug,
    int? YearFrom,
    int? YearTo,
    IReadOnlyCollection<string> Subjects,
    int Count,
    QuestionSource Source,
    bool Sequential);

public enum QuestionSource
{
    All = 0,
    Unseen = 1,
    Wrong = 2,
    Bookmarked = 3
}

public class PracticeService(AppDbContext db, TimeProvider clock)
{
    /// <summary>連續答對幾次後，錯題自動標記為「已熟練」。</summary>
    public const int MasteryStreak = 2;

    /// <summary>間隔複習的間隔（天）：答錯回到第一格，答對往下一格。</summary>
    public static readonly int[] ReviewIntervals = [1, 3, 7, 14, 30];

    public const int MaxQuestions = 100;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>可出題的題目（排除公開模式下不提供內文的試卷）。</summary>
    private IQueryable<Question> Playable => db.Questions.Where(q => !q.Paper!.ContentWithheld);

    public async Task<PracticeSession> CreateFromPaperAsync(string userId, int paperId, bool instant = false, int? timeLimitMinutes = null)
    {
        var paper = await db.Papers.AsNoTracking().FirstAsync(p => p.Id == paperId);
        var ids = await Playable.Where(q => q.PaperId == paperId).OrderBy(q => q.Number).Select(q => q.Id).ToListAsync();
        var s = New(userId, $"{paper.RocYear}年 {paper.ExamTitle}｜{paper.SubjectName}", ids, PracticeMode.Paper, instant, timeLimitMinutes);
        s.PaperId = paperId;
        return await SaveAsync(s);
    }

    /// <summary>依科目名稱跨年度隨機抽題。科目名稱完全相同才算同一科（例如「行政學大意」）。</summary>
    public async Task<PracticeSession?> CreateRandomAsync(string userId, string subjectName, int count, bool unseenOnly, bool instant = false)
    {
        var query = Playable.Where(q => q.Paper!.SubjectName == subjectName);
        if (unseenOnly) query = query.Where(q => !Answered(userId).Contains(q.Id));
        var pool = await query.Select(q => q.Id).ToListAsync();
        if (pool.Count == 0) return null;
        var ids = Shuffle(pool).Take(Math.Clamp(count, 1, MaxQuestions)).ToList();
        return await SaveAsync(New(userId, $"隨機練習｜{subjectName}（{ids.Count} 題）", ids, PracticeMode.Random, instant, null));
    }

    public async Task<PracticeSession?> CreateFromWrongBookAsync(string userId, string? subjectName, int count, bool instant = false)
    {
        var query = db.WrongQuestions.Where(w => w.UserId == userId && !w.Mastered && !w.Question!.Paper!.ContentWithheld);
        if (!string.IsNullOrEmpty(subjectName))
            query = query.Where(w => w.Question!.Paper!.SubjectName == subjectName);
        var pool = await query.OrderByDescending(w => w.WrongCount).ThenBy(w => w.LastWrongAt)
            .Select(w => w.QuestionId).Take(Math.Clamp(count, 1, MaxQuestions)).ToListAsync();
        if (pool.Count == 0) return null;
        var title = string.IsNullOrEmpty(subjectName) ? "錯題重練" : $"錯題重練｜{subjectName}";
        return await SaveAsync(New(userId, $"{title}（{pool.Count} 題）", pool, PracticeMode.WrongBook, instant, null));
    }

    /// <summary>自訂範圍出題：考試、年度區間、科目、題目來源；可循序（依年度與題號）或隨機。</summary>
    public async Task<PracticeSession?> CreateCustomAsync(string userId, CustomQuery c, bool instant, int? timeLimitMinutes)
    {
        var q = Playable;
        if (!string.IsNullOrEmpty(c.ExamSlug)) q = q.Where(x => x.Paper!.Exam!.Slug == c.ExamSlug);
        if (c.YearFrom is { } yf) q = q.Where(x => x.Paper!.RocYear >= yf);
        if (c.YearTo is { } yt) q = q.Where(x => x.Paper!.RocYear <= yt);
        if (c.Subjects.Count > 0) q = q.Where(x => c.Subjects.Contains(x.Paper!.SubjectName));
        q = c.Source switch
        {
            QuestionSource.Unseen => q.Where(x => !Answered(userId).Contains(x.Id)),
            QuestionSource.Wrong => q.Where(x => db.WrongQuestions.Any(w => w.UserId == userId && w.QuestionId == x.Id && !w.Mastered)),
            QuestionSource.Bookmarked => q.Where(x => db.Bookmarks.Any(b => b.UserId == userId && b.QuestionId == x.Id)),
            _ => q
        };
        var take = Math.Clamp(c.Count, 1, MaxQuestions);
        List<int> ids;
        if (c.Sequential)
        {
            ids = await q.OrderBy(x => x.Paper!.RocYear).ThenBy(x => x.Paper!.SubjectName).ThenBy(x => x.PaperId).ThenBy(x => x.Number)
                .Select(x => x.Id).Take(take).ToListAsync();
        }
        else
        {
            ids = Shuffle(await q.Select(x => x.Id).ToListAsync()).Take(take).ToList();
        }
        if (ids.Count == 0) return null;
        var scope = c.Subjects.Count == 1 ? c.Subjects.First() : c.Subjects.Count > 1 ? $"{c.Subjects.Count} 科" : "全部科目";
        var label = c.Sequential ? "循序練習" : "自訂練習";
        return await SaveAsync(New(userId, $"{label}｜{scope}（{ids.Count} 題）", ids, PracticeMode.Custom, instant, timeLimitMinutes));
    }

    /// <summary>到期的錯題複習（間隔複習）。</summary>
    public async Task<PracticeSession?> CreateReviewAsync(string userId, int count)
    {
        var ids = await DueQuery(userId).OrderBy(w => w.DueAt).Select(w => w.QuestionId)
            .Take(Math.Clamp(count, 1, MaxQuestions)).ToListAsync();
        if (ids.Count == 0) return null;
        return await SaveAsync(New(userId, $"今日複習（{ids.Count} 題）", ids, PracticeMode.Review, instant: true, null));
    }

    public IQueryable<WrongQuestion> DueQuery(string userId)
    {
        var now = Now;
        return db.WrongQuestions.Where(w => w.UserId == userId && !w.Mastered && !w.Question!.Paper!.ContentWithheld
                                            && (w.DueAt == null || w.DueAt <= now));
    }

    /// <summary>
    /// 每日推薦：先排到期複習（最多一半），其餘從正確率最低的科目挑沒做過的題目；
    /// 作答資料不足時，從讀書計畫中的科目或全部題庫補足。
    /// </summary>
    public async Task<PracticeSession?> CreateDailyAsync(string userId, int count)
    {
        count = Math.Clamp(count, 5, MaxQuestions);
        var picked = await DueQuery(userId).OrderBy(w => w.DueAt).Select(w => w.QuestionId).Take(count / 2).ToListAsync();

        var weak = await WeakSubjectsAsync(userId, 3);
        var unseen = Playable.Where(q => !Answered(userId).Contains(q.Id) && !picked.Contains(q.Id));
        if (weak.Count > 0)
        {
            var pool = await unseen.Where(q => weak.Contains(q.Paper!.SubjectName)).Select(q => q.Id).ToListAsync();
            picked.AddRange(Shuffle(pool).Take(count - picked.Count));
        }
        if (picked.Count < count)
        {
            var planned = await db.StudySubjects.Where(s => s.UserId == userId && !s.Archived).Select(s => s.Name).ToListAsync();
            var q = planned.Count > 0 ? unseen.Where(x => planned.Contains(x.Paper!.SubjectName)) : unseen;
            var pool = await q.Where(x => !picked.Contains(x.Id)).Select(x => x.Id).Take(5000).ToListAsync();
            if (pool.Count == 0 && planned.Count > 0)
                pool = await unseen.Where(x => !picked.Contains(x.Id)).Select(x => x.Id).Take(5000).ToListAsync();
            picked.AddRange(Shuffle(pool).Take(count - picked.Count));
        }
        if (picked.Count == 0) return null;
        return await SaveAsync(New(userId, $"每日推薦（{picked.Count} 題）", picked, PracticeMode.Daily, instant: true, null));
    }

    /// <summary>正確率最低的科目（每科至少作答 5 題才列入）。</summary>
    public async Task<List<string>> WeakSubjectsAsync(string userId, int take)
    {
        var stats = await db.PracticeAnswers.Where(a => a.Session!.UserId == userId)
            .GroupBy(a => a.Question!.Paper!.SubjectName)
            .Select(g => new { g.Key, N = g.Count(), C = g.Count(a => a.IsCorrect) })
            .Where(x => x.N >= 5).ToListAsync();
        return stats.OrderBy(x => (double)x.C / x.N).Take(take).Select(x => x.Key).ToList();
    }

    private IQueryable<int> Answered(string userId) =>
        db.PracticeAnswers.Where(a => a.Session!.UserId == userId).Select(a => a.QuestionId);

    private static List<int> Shuffle(List<int> ids) => ids.OrderBy(_ => Random.Shared.Next()).ToList();

    private PracticeSession New(string userId, string title, List<int> ids, PracticeMode mode, bool instant, int? limit) => new()
    {
        UserId = userId,
        Title = title,
        StartedAt = Now,
        TotalCount = ids.Count,
        QuestionIds = string.Join(',', ids),
        Mode = mode,
        Instant = instant,
        TimeLimitMinutes = limit is > 0 ? Math.Min(limit.Value, 300) : null
    };

    private async Task<PracticeSession> SaveAsync(PracticeSession s)
    {
        db.PracticeSessions.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    public List<int> QuestionIdsOf(PracticeSession s) =>
        s.QuestionIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();

    public async Task<List<Question>> LoadQuestionsAsync(PracticeSession s)
    {
        var ids = QuestionIdsOf(s);
        var map = await db.Questions.Include(q => q.Paper).Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id);
        return ids.Where(map.ContainsKey).Select(id => map[id]).ToList();
    }

    /// <summary>逐題模式：作答一題並立即回傳結果。同一題重複送出時沿用第一次的答案。</summary>
    public async Task<PracticeAnswer?> AnswerOneAsync(PracticeSession s, int questionId, string? raw)
    {
        if (s.FinishedAt is not null || !QuestionIdsOf(s).Contains(questionId)) return null;
        await db.Entry(s).Collection(x => x.Answers).LoadAsync();
        var existing = s.Answers.FirstOrDefault(a => a.QuestionId == questionId);
        if (existing is not null) return existing;
        var q = await db.Questions.FirstAsync(x => x.Id == questionId);
        var wrong = await db.WrongQuestions.FirstOrDefaultAsync(w => w.UserId == s.UserId && w.QuestionId == questionId);
        var a = Record(s, q, raw, wrong);
        await db.SaveChangesAsync();
        if (s.Answers.Count >= QuestionIdsOf(s).Count) await FinishAsync(s);
        return a;
    }

    /// <summary>交卷：批改尚未作答的題目（未作答視為答錯），並結算成績。</summary>
    public async Task GradeAsync(PracticeSession s, IDictionary<int, string> selections)
    {
        if (s.FinishedAt is not null) return;
        await db.Entry(s).Collection(x => x.Answers).LoadAsync();
        var questions = await LoadQuestionsAsync(s);
        var ids = questions.Select(q => q.Id).ToList();
        var wrongBook = await db.WrongQuestions.Where(w => w.UserId == s.UserId && ids.Contains(w.QuestionId))
            .ToDictionaryAsync(w => w.QuestionId);
        var done = s.Answers.Select(a => a.QuestionId).ToHashSet();
        foreach (var q in questions.Where(q => !done.Contains(q.Id)))
        {
            selections.TryGetValue(q.Id, out var raw);
            wrongBook.TryGetValue(q.Id, out var w);
            Record(s, q, raw, w);
        }
        await FinishAsync(s);
    }

    private async Task FinishAsync(PracticeSession s)
    {
        s.CorrectCount = s.Answers.Count(a => a.IsCorrect);
        s.TotalCount = QuestionIdsOf(s).Count;
        s.FinishedAt = Now;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 記錄一題作答並更新錯題本：
    /// 答錯 → 加入／累計錯誤、連對歸零、隔天複習；
    /// 在錯題本中且答對 → 連對 +1、複習間隔往後推；連對達門檻即標記熟練。
    /// </summary>
    private PracticeAnswer Record(PracticeSession s, Question q, string? raw, WrongQuestion? w)
    {
        var now = Now;
        var selected = AnswerChecker.Normalize(raw);
        var ok = AnswerChecker.IsCorrect(selected, q.Answer);
        var a = new PracticeAnswer { QuestionId = q.Id, Selected = selected, IsCorrect = ok, AnsweredAt = now };
        s.Answers.Add(a);
        if (!ok)
        {
            if (w is null)
            {
                w = new WrongQuestion { UserId = s.UserId, QuestionId = q.Id };
                db.WrongQuestions.Add(w);
            }
            w.WrongCount++;
            w.CorrectStreak = 0;
            w.Mastered = false;
            w.LastWrongAt = now;
            w.IntervalDays = ReviewIntervals[0];
            w.DueAt = now.AddDays(w.IntervalDays);
        }
        else if (w is not null && !w.Mastered)
        {
            w.CorrectStreak++;
            w.IntervalDays = NextInterval(w.IntervalDays);
            w.DueAt = now.AddDays(w.IntervalDays);
            if (w.CorrectStreak >= MasteryStreak)
            {
                w.Mastered = true;
                w.DueAt = null;
            }
        }
        return a;
    }

    public static int NextInterval(int current)
    {
        foreach (var d in ReviewIntervals)
            if (d > current) return d;
        return ReviewIntervals[^1];
    }
}
