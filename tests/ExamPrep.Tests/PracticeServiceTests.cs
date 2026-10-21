using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Tests;

public class PracticeServiceTests
{
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (AppDbContext db, PracticeService svc, QuestionPaper paper) Setup()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var paper = new QuestionPaper
        {
            ExamCode = "T", PaperCode = "1", RocYear = 115, SubjectName = "行政學大意", ExamTitle = "測試",
            Questions =
            [
                new Question { Number = 1, Stem = "q1", OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d", Answer = "C" },
                new Question { Number = 2, Stem = "q2", OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d", OptionE = "e", Answer = "BD", Type = QuestionType.Multiple },
            ]
        };
        db.Papers.Add(paper);
        db.SaveChanges();
        return (db, new PracticeService(db, new FixedClock(new DateTimeOffset(2026, 10, 21, 4, 0, 0, TimeSpan.Zero))), paper);
    }

    [Fact]
    public async Task Grading_counts_correct_and_adds_wrong_to_book()
    {
        var (db, svc, paper) = Setup();
        var s = await svc.CreateFromPaperAsync("u1", paper.Id);
        var q1 = paper.Questions[0].Id; var q2 = paper.Questions[1].Id;

        await svc.GradeAsync(s, new Dictionary<int, string> { [q1] = "C", [q2] = "B" });

        Assert.Equal(1, s.CorrectCount);
        var w = Assert.Single(db.WrongQuestions);
        Assert.Equal(q2, w.QuestionId);
        Assert.Equal(1, w.WrongCount);
    }

    [Fact]
    public async Task Grading_twice_is_ignored()
    {
        var (db, svc, paper) = Setup();
        var s = await svc.CreateFromPaperAsync("u1", paper.Id);
        await svc.GradeAsync(s, new Dictionary<int, string>());
        await svc.GradeAsync(s, new Dictionary<int, string>());
        Assert.Equal(2, db.PracticeAnswers.Count());
        Assert.All(db.WrongQuestions, w => Assert.Equal(1, w.WrongCount));
    }

    [Fact]
    public async Task Wrong_question_is_mastered_after_consecutive_correct_answers_and_reset_on_miss()
    {
        var (db, svc, paper) = Setup();
        var q2 = paper.Questions[1].Id;
        async Task Answer(string sel)
        {
            var s = await svc.CreateFromPaperAsync("u1", paper.Id);
            await svc.GradeAsync(s, new Dictionary<int, string> { [q2] = sel });
        }

        await Answer("B");                 // 錯 → 進錯題本
        await Answer("BD");                // 連對 1
        await Answer("D");                 // 錯 → 連對歸零
        await Answer("DB");                // 連對 1
        var w = db.WrongQuestions.Single(x => x.QuestionId == q2);
        Assert.False(w.Mastered);
        Assert.Equal(2, w.WrongCount);

        await Answer("BD");                // 連對 2 → 熟練
        Assert.True(db.WrongQuestions.Single(x => x.QuestionId == q2).Mastered);
    }

    [Fact]
    public async Task Users_do_not_share_wrong_books()
    {
        var (db, svc, paper) = Setup();
        var s = await svc.CreateFromPaperAsync("u1", paper.Id);
        await svc.GradeAsync(s, new Dictionary<int, string>());
        Assert.Null(await svc.CreateFromWrongBookAsync("u2", null, 10));
        Assert.NotNull(await svc.CreateFromWrongBookAsync("u1", null, 10));
    }

    [Fact]
    public void Taipei_today_differs_from_utc_date_after_4pm_utc()
    {
        var date = new TaipeiDate(new FixedClock(new DateTimeOffset(2026, 10, 21, 16, 30, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 10, 22), date.Today);
        Assert.Equal(1, date.DaysUntil(new DateOnly(2026, 10, 23)));
        Assert.Equal("116/1/9（六）", TaipeiDate.RocWithWeekday(new DateOnly(2027, 1, 9)));
    }
}
