using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Services;

public class CountdownService(AppDbContext db, TaipeiDate date)
{
    public async Task<List<CountdownItem>> AllAsync(string? userId, string? organizer = null)
    {
        var q = db.ExamSessions.Include(s => s.Exam).AsNoTracking();
        if (!string.IsNullOrEmpty(organizer)) q = q.Where(s => s.Exam!.Organizer == organizer);
        var sessions = await q.ToListAsync();
        var targets = userId is null
            ? new HashSet<int>()
            : (await db.UserTargetExams.Where(t => t.UserId == userId).Select(t => t.ExamSessionId).ToListAsync()).ToHashSet();
        return sessions.Select(s => ToItem(s, targets.Contains(s.Id)))
            .OrderBy(i => i.Session.ExamStart).ToList();
    }

    public CountdownItem ToItem(ExamSession s, bool isTarget) => new(
        s,
        date.DaysUntil(s.ExamStart),
        s.RegistrationEnd is { } r ? date.DaysUntil(r) : null,
        isTarget);

    /// <summary>考試結束日已過即視為「已結束」。</summary>
    public bool IsPast(ExamSession s) => date.DaysUntil(s.ExamEnd) < 0;
}
