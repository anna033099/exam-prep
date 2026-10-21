using ExamPrep.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Services;

public record DailyProgress(int AnsweredToday, int Goal, int StreakDays, int DueReviews);

public record LeaderRow(string DisplayName, int Answered, int Correct, bool IsMe)
{
    public double Rate => Answered == 0 ? 0 : (double)Correct / Answered;
}

/// <summary>刷題統計：以台灣時間切日。</summary>
public class StatsService(AppDbContext db, TaipeiDate date, PracticeService practice)
{
    private static readonly TimeSpan Offset = TimeSpan.FromHours(8);

    /// <summary>台灣某日 00:00 對應的 UTC 時間。</summary>
    public static DateTime UtcStartOf(DateOnly d) => d.ToDateTime(TimeOnly.MinValue) - Offset;

    public async Task<DailyProgress> DailyAsync(string userId)
    {
        var today = date.Today;
        var goal = (await db.UserSettings.FindAsync(userId))?.DailyGoalQuestions ?? 20;
        var since = UtcStartOf(today.AddDays(-60));
        // 只計算真的有選答案的題目；交卷時留白、被判答錯的題目不算「刷過」
        var times = await db.PracticeAnswers.Where(a => a.Session!.UserId == userId && a.AnsweredAt >= since && a.Selected != "")
            .Select(a => a.AnsweredAt).ToListAsync();
        var days = times.Select(t => DateOnly.FromDateTime(t + Offset)).ToHashSet();
        var answeredToday = times.Count(t => DateOnly.FromDateTime(t + Offset) == today);
        int streak = 0;
        for (var d = days.Contains(today) ? today : today.AddDays(-1); days.Contains(d); d = d.AddDays(-1)) streak++;
        var due = await practice.DueQuery(userId).CountAsync();
        return new DailyProgress(answeredToday, goal, streak, due);
    }

    /// <summary>本週（週一起算，台灣時間）作答排行；只列出選擇公開的使用者，本人一律可見自己的名次。</summary>
    public async Task<(List<LeaderRow> ByCount, List<LeaderRow> ByRate, DateOnly WeekStart)> WeeklyAsync(string? me, int minForRate = 50)
    {
        var today = date.Today;
        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        var since = UtcStartOf(weekStart);
        var visible = db.UserSettings.Where(s => s.ShowOnLeaderboard).Select(s => s.UserId);
        var rows = await db.PracticeAnswers.Where(a => a.AnsweredAt >= since && a.Selected != "")
            .Where(a => visible.Contains(a.Session!.UserId) || a.Session!.UserId == me)
            .GroupBy(a => a.Session!.UserId)
            .Select(g => new { UserId = g.Key, N = g.Count(), C = g.Count(a => a.IsCorrect) })
            .ToListAsync();
        var ids = rows.Select(r => r.UserId).ToList();
        var names = await db.Users.Where(u => ids.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.DisplayName);
        var all = rows.Select(r => new LeaderRow(string.IsNullOrWhiteSpace(names.GetValueOrDefault(r.UserId)) ? "匿名考生" : names[r.UserId],
            r.N, r.C, r.UserId == me)).ToList();
        return (all.OrderByDescending(r => r.Answered).Take(30).ToList(),
                all.Where(r => r.Answered >= minForRate).OrderByDescending(r => r.Rate).ThenByDescending(r => r.Answered).Take(30).ToList(),
                weekStart);
    }
}
