using ExamPrep.Web.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Exam> Exams => Set<Exam>();
    public DbSet<ExamSession> ExamSessions => Set<ExamSession>();
    public DbSet<ExamSubjectInfo> ExamSubjects => Set<ExamSubjectInfo>();
    public DbSet<QuestionPaper> Papers => Set<QuestionPaper>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<PracticeSession> PracticeSessions => Set<PracticeSession>();
    public DbSet<PracticeAnswer> PracticeAnswers => Set<PracticeAnswer>();
    public DbSet<WrongQuestion> WrongQuestions => Set<WrongQuestion>();
    public DbSet<UserTargetExam> UserTargetExams => Set<UserTargetExam>();
    public DbSet<StudySubject> StudySubjects => Set<StudySubject>();
    public DbSet<StudyLog> StudyLogs => Set<StudyLog>();
    public DbSet<UserSetting> UserSettings => Set<UserSetting>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Exam>().HasIndex(e => e.Slug).IsUnique();

        b.Entity<QuestionPaper>().HasIndex(p => new { p.ExamCode, p.PaperCode }).IsUnique();
        b.Entity<QuestionPaper>().HasIndex(p => p.SubjectName);
        b.Entity<QuestionPaper>().HasOne(p => p.Exam).WithMany().OnDelete(DeleteBehavior.SetNull);

        b.Entity<Question>().HasIndex(q => new { q.PaperId, q.Number }).IsUnique();

        b.Entity<PracticeSession>().HasIndex(s => s.UserId);
        b.Entity<PracticeAnswer>().HasOne(a => a.Question).WithMany().OnDelete(DeleteBehavior.Restrict);

        b.Entity<WrongQuestion>().HasIndex(w => new { w.UserId, w.QuestionId }).IsUnique();
        b.Entity<WrongQuestion>().HasOne(w => w.Question).WithMany().OnDelete(DeleteBehavior.Cascade);

        b.Entity<UserTargetExam>().HasIndex(t => new { t.UserId, t.ExamSessionId }).IsUnique();

        b.Entity<StudyLog>().HasIndex(l => new { l.UserId, l.Date });
        b.Entity<StudyLog>().HasOne(l => l.StudySubject).WithMany(s => s.Logs).OnDelete(DeleteBehavior.Cascade);
    }
}
