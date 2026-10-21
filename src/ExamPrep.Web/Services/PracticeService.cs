using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Services;

public class PracticeService(AppDbContext db, TimeProvider clock)
{
    /// <summary>連續答對幾次後，錯題自動標記為「已熟練」。</summary>
    public const int MasteryStreak = 2;

    public async Task<PracticeSession> CreateFromPaperAsync(string userId, int paperId)
    {
        var paper = await db.Papers.AsNoTracking().FirstAsync(p => p.Id == paperId);
        var ids = await db.Questions.Where(q => q.PaperId == paperId).OrderBy(q => q.Number)
            .Select(q => q.Id).ToListAsync();
        return await SaveAsync(userId, $"{paper.RocYear}年 {paper.ExamTitle}｜{paper.SubjectName}", ids);
    }

    /// <summary>依科目名稱跨年度隨機抽題。科目名稱完全相同才算同一科（例如「行政學大意」）。</summary>
    public async Task<PracticeSession?> CreateRandomAsync(string userId, string subjectName, int count, bool unseenOnly)
    {
        var query = db.Questions.Where(q => q.Paper!.SubjectName == subjectName);
        if (unseenOnly)
        {
            var answered = db.PracticeAnswers.Where(a => a.Session!.UserId == userId).Select(a => a.QuestionId);
            query = query.Where(q => !answered.Contains(q.Id));
        }
        var pool = await query.Select(q => q.Id).ToListAsync();
        if (pool.Count == 0) return null;
        var ids = pool.OrderBy(_ => Random.Shared.Next()).Take(Math.Clamp(count, 1, 100)).ToList();
        return await SaveAsync(userId, $"隨機練習｜{subjectName}（{ids.Count} 題）", ids);
    }

    public async Task<PracticeSession?> CreateFromWrongBookAsync(string userId, string? subjectName, int count)
    {
        var query = db.WrongQuestions.Where(w => w.UserId == userId && !w.Mastered);
        if (!string.IsNullOrEmpty(subjectName))
            query = query.Where(w => w.Question!.Paper!.SubjectName == subjectName);
        var pool = await query.OrderByDescending(w => w.WrongCount).ThenBy(w => w.LastWrongAt)
            .Select(w => w.QuestionId).Take(Math.Clamp(count, 1, 100)).ToListAsync();
        if (pool.Count == 0) return null;
        var title = string.IsNullOrEmpty(subjectName) ? "錯題重練" : $"錯題重練｜{subjectName}";
        return await SaveAsync(userId, $"{title}（{pool.Count} 題）", pool);
    }

    private async Task<PracticeSession> SaveAsync(string userId, string title, List<int> ids)
    {
        var s = new PracticeSession
        {
            UserId = userId,
            Title = title,
            StartedAt = clock.GetUtcNow().UtcDateTime,
            TotalCount = ids.Count,
            QuestionIds = string.Join(',', ids)
        };
        db.PracticeSessions.Add(s);
        await db.SaveChangesAsync();
        return s;
    }

    public async Task<List<Question>> LoadQuestionsAsync(PracticeSession s)
    {
        var ids = s.QuestionIds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        var map = await db.Questions.Include(q => q.Paper).Where(q => ids.Contains(q.Id)).ToDictionaryAsync(q => q.Id);
        return ids.Where(map.ContainsKey).Select(id => map[id]).ToList();
    }

    /// <summary>批改並更新錯題本。答錯→加入/累計；在錯題本中的題目答對→累計連對，達門檻即標記熟練。</summary>
    public async Task GradeAsync(PracticeSession s, IDictionary<int, string> selections)
    {
        if (s.FinishedAt is not null) return;
        var questions = await LoadQuestionsAsync(s);
        var now = clock.GetUtcNow().UtcDateTime;
        var wrongBook = await db.WrongQuestions
            .Where(w => w.UserId == s.UserId && questions.Select(q => q.Id).Contains(w.QuestionId))
            .ToDictionaryAsync(w => w.QuestionId);

        int correct = 0;
        foreach (var q in questions)
        {
            selections.TryGetValue(q.Id, out var raw);
            var selected = AnswerChecker.Normalize(raw);
            var ok = AnswerChecker.IsCorrect(selected, q.Answer);
            if (ok) correct++;
            s.Answers.Add(new PracticeAnswer { QuestionId = q.Id, Selected = selected, IsCorrect = ok });

            wrongBook.TryGetValue(q.Id, out var w);
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
            }
            else if (w is not null && !w.Mastered)
            {
                w.CorrectStreak++;
                if (w.CorrectStreak >= MasteryStreak) w.Mastered = true;
            }
        }
        s.CorrectCount = correct;
        s.FinishedAt = now;
        await db.SaveChangesAsync();
    }
}
