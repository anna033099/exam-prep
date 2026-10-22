using System.Net;
using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamPrep.Tests;

public class PracticeModesTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 21, 4, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static (AppDbContext db, PracticeService svc, Clock clock, List<Question> qs) Setup()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var exam = new Exam { Slug = "chudeng", Name = "初等", ShortName = "初等" };
        var hidden = new Exam { Slug = "post", Name = "郵局", ShortName = "郵局" };
        db.Exams.AddRange(exam, hidden);
        Question Q(int n, string ans = "A") => new() { Number = n, Stem = $"q{n}", OptionA = "a", OptionB = "b", OptionC = "c", OptionD = "d", Answer = ans };
        var p113 = new QuestionPaper { ExamCode = "113010", PaperCode = "1", RocYear = 113, SubjectName = "行政學大意", Exam = exam, Questions = [Q(1), Q(2), Q(3)] };
        var p115 = new QuestionPaper { ExamCode = "115010", PaperCode = "2", RocYear = 115, SubjectName = "行政學大意", Exam = exam, Questions = [Q(1), Q(2)] };
        var law = new QuestionPaper { ExamCode = "115010", PaperCode = "3", RocYear = 115, SubjectName = "法學大意", Exam = exam, Questions = [Q(1), Q(2)] };
        var withheld = new QuestionPaper { ExamCode = "POST-114", PaperCode = "4", RocYear = 114, SubjectName = "行政學大意", Exam = hidden, ContentWithheld = true, Questions = [Q(1)] };
        db.Papers.AddRange(p113, p115, law, withheld);
        db.SaveChanges();
        foreach (var p in new[] { p113, p115, law, withheld })
            db.PaperExamLinks.Add(new PaperExamLink { PaperId = p.Id, ExamId = p.Exam!.Id });
        db.SaveChanges();
        var clock = new Clock();
        return (db, new PracticeService(db, clock), clock, db.Questions.OrderBy(q => q.Id).ToList());
    }

    [Fact]
    public async Task Review_interval_grows_on_correct_and_resets_on_wrong()
    {
        var (db, svc, clock, qs) = Setup();
        var q = qs[0];
        async Task Answer(string sel)
        {
            var s = await svc.CreateCustomAsync("u", new CustomQuery(null, 113, 113, ["行政學大意"], 1, QuestionSource.All, true), true, null);
            await svc.AnswerOneAsync(s!, q.Id, sel);
        }

        await Answer("B");
        var w = db.WrongQuestions.Single();
        Assert.Equal(1, w.IntervalDays);
        Assert.Equal(clock.Now.UtcDateTime.AddDays(1), w.DueAt);
        Assert.Empty(svc.DueQuery("u"));                 // 還沒到期

        clock.Now = clock.Now.AddDays(1);
        Assert.Single(svc.DueQuery("u"));                // 隔天到期
        await Answer("A");
        Assert.Equal(3, w.IntervalDays);                 // 1 → 3
        await Answer("B");
        Assert.Equal(1, w.IntervalDays);                 // 答錯回到第一格
        Assert.Equal(0, w.CorrectStreak);
    }

    [Fact]
    public async Task Instant_answers_are_kept_and_session_finishes_after_last_question()
    {
        var (db, svc, _, qs) = Setup();
        var paper = db.Papers.First(p => p.PaperCode == "2");
        var s = await svc.CreateFromPaperAsync("u", paper.Id, instant: true);
        var ids = svc.QuestionIdsOf(s);

        var a1 = await svc.AnswerOneAsync(s, ids[0], "A");
        Assert.True(a1!.IsCorrect);
        var again = await svc.AnswerOneAsync(s, ids[0], "B");   // 重送不會改變答案
        Assert.True(again!.IsCorrect);
        Assert.Null(s.FinishedAt);

        await svc.AnswerOneAsync(s, ids[1], "C");
        Assert.NotNull(s.FinishedAt);
        Assert.Equal(1, s.CorrectCount);
        Assert.Null(await svc.AnswerOneAsync(s, ids[1], "A"));  // 已結束
    }

    [Fact]
    public async Task Submitting_instant_session_grades_only_unanswered_questions()
    {
        var (db, svc, _, _) = Setup();
        var paper = db.Papers.First(p => p.PaperCode == "1");
        var s = await svc.CreateFromPaperAsync("u", paper.Id, instant: true);
        var ids = svc.QuestionIdsOf(s);
        await svc.AnswerOneAsync(s, ids[0], "A");
        await svc.GradeAsync(s, new Dictionary<int, string> { [ids[0]] = "D", [ids[1]] = "A" });
        Assert.Equal(3, s.Answers.Count);
        Assert.Equal(2, s.CorrectCount);   // 第 1 題沿用逐題時的正確答案，第 2 題答對，第 3 題未作答
    }

    [Fact]
    public async Task Custom_query_filters_by_year_subject_and_excludes_withheld()
    {
        var (db, svc, _, _) = Setup();
        var s = await svc.CreateCustomAsync("u", new CustomQuery(null, 114, 115, ["行政學大意"], 50, QuestionSource.All, true), false, 45);
        var qs = await svc.LoadQuestionsAsync(s!);
        Assert.Equal(2, qs.Count);
        Assert.All(qs, q => Assert.Equal("2", q.Paper!.PaperCode));   // 113 年被排除，郵局（不提供內文）被排除
        Assert.Equal(45, s!.TimeLimitMinutes);
        Assert.Equal([1, 2], qs.Select(q => q.Number));
    }

    [Fact]
    public async Task Bookmarked_and_unseen_sources()
    {
        var (db, svc, _, qs) = Setup();
        db.Bookmarks.Add(new QuestionBookmark { UserId = "u", QuestionId = qs[3].Id });
        db.SaveChanges();
        var b = await svc.CreateCustomAsync("u", new CustomQuery(null, null, null, [], 10, QuestionSource.Bookmarked, false), false, null);
        Assert.Equal([qs[3].Id], svc.QuestionIdsOf(b!));

        var all = await svc.CreateCustomAsync("u", new CustomQuery("chudeng", null, null, [], 100, QuestionSource.All, true), false, null);
        await svc.GradeAsync(all!, new Dictionary<int, string>());
        Assert.Null(await svc.CreateCustomAsync("u", new CustomQuery("chudeng", null, null, [], 10, QuestionSource.Unseen, false), false, null));
    }

    [Fact]
    public async Task Daily_set_prefers_due_reviews_then_weak_subjects()
    {
        var (db, svc, clock, qs) = Setup();
        var first = await svc.CreateCustomAsync("u", new CustomQuery(null, null, null, ["法學大意"], 10, QuestionSource.All, true), false, null);
        await svc.GradeAsync(first!, new Dictionary<int, string>());   // 法學大意 2 題全錯
        clock.Now = clock.Now.AddDays(2);
        var daily = await svc.CreateDailyAsync("u", 6);
        var ids = svc.QuestionIdsOf(daily!);
        var lawIds = qs.Where(q => db.Papers.Find(q.PaperId)!.SubjectName == "法學大意").Select(q => q.Id).ToList();
        Assert.Equal(lawIds, ids.Take(2).Order().ToList());   // 到期錯題排在最前面
        Assert.DoesNotContain(qs.Single(q => db.Papers.Find(q.PaperId)!.ContentWithheld).Id, ids);
        Assert.True(daily!.Instant);
    }

    [Fact]
    public async Task Essay_questions_are_never_put_into_practice_sessions()
    {
        var (db, svc, _, _) = Setup();
        var paper = db.Papers.First(p => p.PaperCode == "2");
        db.Questions.Add(new Question { PaperId = paper.Id, Number = Question.EssayNumberBase + 1, Type = QuestionType.Essay, Stem = "一、請說明比例原則。（25分）" });
        db.SaveChanges();
        var s = await svc.CreateFromPaperAsync("u", paper.Id);
        Assert.Equal(2, svc.QuestionIdsOf(s).Count);
        var c = await svc.CreateCustomAsync("u", new CustomQuery(null, null, null, [], 50, QuestionSource.All, true), false, null);
        Assert.DoesNotContain(db.Questions.Single(q => q.Type == QuestionType.Essay).Id, svc.QuestionIdsOf(c!));
        Assert.Equal("申論第一題", db.Questions.Single(q => q.Type == QuestionType.Essay).Label);
        Assert.Equal("二十三", Question.CnNumber(23));
    }

    [Fact]
    public void Next_interval_steps_and_caps()
    {
        Assert.Equal(1, PracticeService.NextInterval(0));
        Assert.Equal(3, PracticeService.NextInterval(1));
        Assert.Equal(30, PracticeService.NextInterval(14));
        Assert.Equal(30, PracticeService.NextInterval(30));
    }

    [Fact]
    public async Task Daily_progress_counts_only_answered_questions_in_taipei_day()
    {
        var (db, svc, clock, _) = Setup();
        clock.Now = new DateTimeOffset(2026, 10, 21, 17, 0, 0, TimeSpan.Zero);   // 台灣時間 10/22 01:00
        var paper = db.Papers.First(p => p.PaperCode == "1");
        var s = await svc.CreateFromPaperAsync("u", paper.Id);
        var ids = svc.QuestionIdsOf(s);
        await svc.GradeAsync(s, new Dictionary<int, string> { [ids[0]] = "A" });   // 只答 1 題，另外 2 題留白
        var stats = new StatsService(db, new TaipeiDate(clock), svc);
        var d = await stats.DailyAsync("u");
        Assert.Equal(1, d.AnsweredToday);
        Assert.Equal(1, d.StreakDays);
        Assert.Equal(3, s.TotalCount);
    }

    // ---------- AI 詳解 ----------

    private sealed class FakeHandler(HttpStatusCode code, string body) : HttpMessageHandler
    {
        public int Calls;
        public string? LastRequest;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastRequest = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(code) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };
        }
    }

    private static AiExplanationService Ai(AppDbContext db, FakeHandler h, string? key = "k", int limit = 10) =>
        new(db, new HttpClient(h), Options.Create(new AiOptions { AnthropicApiKey = key, DailyLimitPerUser = limit }), new Clock(), NullLogger<AiExplanationService>.Instance);

    [Fact]
    public async Task Ai_explanation_is_generated_once_and_cached()
    {
        var (db, _, _, qs) = Setup();
        var h = new FakeHandler(HttpStatusCode.OK, """{"content":[{"type":"text","text":"關鍵觀念……"}]}""");
        var ai = Ai(db, h);
        var r1 = await ai.GetOrCreateAsync(qs[0].Id, "u");
        var r2 = await ai.GetOrCreateAsync(qs[0].Id, "u2");
        Assert.Equal(AiResultStatus.Ok, r1.Status);
        Assert.Equal(AiResultStatus.Cached, r2.Status);
        Assert.Equal(1, h.Calls);
        var sent = System.Text.Json.JsonDocument.Parse(h.LastRequest!).RootElement;
        Assert.Contains("官方標準答案：A", sent.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Contains("不可推翻", sent.GetProperty("system").GetString());
    }

    [Fact]
    public async Task Ai_is_disabled_without_key_limited_per_day_and_skips_withheld()
    {
        var (db, _, _, qs) = Setup();
        var h = new FakeHandler(HttpStatusCode.OK, """{"content":[{"type":"text","text":"x"}]}""");
        Assert.Equal(AiResultStatus.Disabled, (await Ai(db, h, key: null).GetOrCreateAsync(qs[0].Id, "u")).Status);

        var limited = Ai(db, h, limit: 1);
        Assert.Equal(AiResultStatus.Ok, (await limited.GetOrCreateAsync(qs[0].Id, "u")).Status);
        Assert.Equal(AiResultStatus.LimitReached, (await limited.GetOrCreateAsync(qs[1].Id, "u")).Status);

        var withheldId = qs.Single(q => db.Papers.Find(q.PaperId)!.ContentWithheld).Id;
        Assert.Equal(AiResultStatus.Failed, (await Ai(db, h).GetOrCreateAsync(withheldId, "v")).Status);
    }

    [Fact]
    public async Task Ai_api_error_is_reported_not_cached()
    {
        var (db, _, _, qs) = Setup();
        var r = await Ai(db, new FakeHandler(HttpStatusCode.InternalServerError, "{}")).GetOrCreateAsync(qs[0].Id, "u");
        Assert.Equal(AiResultStatus.Failed, r.Status);
        Assert.Empty(db.Explanations);
    }
}
