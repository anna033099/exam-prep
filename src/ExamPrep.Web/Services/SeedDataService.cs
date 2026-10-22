using System.Text.Json;
using System.Text.Json.Serialization;
using ExamPrep.Web.Data;
using ExamPrep.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace ExamPrep.Web.Services;

/// <summary>
/// 從 SeedData 資料夾匯入考試資訊與官方試題包。可重複執行：已存在的資料以代碼比對後略過或更新。
/// 試題包由 tools/moex 下的解析程式從考選部官方 PDF 產生，每題都附來源連結。
/// </summary>
public class SeedDataService(AppDbContext db, IWebHostEnvironment env, ILogger<SeedDataService> log, Microsoft.Extensions.Options.IOptions<SiteOptions> site)
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private string SeedDir => Path.Combine(env.ContentRootPath, "SeedData");

    public sealed record ImportReport(int ExamsUpserted, int SessionsAdded, int PapersAdded, int QuestionsAdded, int PapersSkipped);

    public async Task<ImportReport> ImportExamInfoAsync(CancellationToken ct = default)
    {
        var path = Path.Combine(SeedDir, "exams.json");
        if (!File.Exists(path)) return new(0, 0, 0, 0, 0);
        var seed = JsonSerializer.Deserialize<ExamSeedFile>(await File.ReadAllTextAsync(path, ct), Json)!;
        int upserted = 0, sessionsAdded = 0;
        foreach (var e in seed.Exams)
        {
            var exam = await db.Exams.Include(x => x.Sessions).Include(x => x.Subjects)
                .FirstOrDefaultAsync(x => x.Slug == e.Slug, ct);
            if (exam is null)
            {
                exam = new Exam { Slug = e.Slug };
                db.Exams.Add(exam);
            }
            exam.Name = e.Name;
            exam.ShortName = e.ShortName;
            exam.Organizer = e.Organizer;
            exam.OrganizerType = e.OrganizerType;
            exam.Summary = e.Summary;
            exam.OfficialUrl = e.OfficialUrl;
            exam.SortOrder = e.SortOrder;
            exam.Group = e.Group;
            upserted++;

            foreach (var s in e.Sessions)
            {
                var existing = exam.Sessions.FirstOrDefault(x => x.Title == s.Title);
                if (existing is null)
                {
                    existing = new ExamSession { Title = s.Title };
                    exam.Sessions.Add(existing);
                    sessionsAdded++;
                }
                existing.RegistrationStart = s.RegistrationStart;
                existing.RegistrationEnd = s.RegistrationEnd;
                existing.ExamStart = s.ExamStart;
                existing.ExamEnd = s.ExamEnd ?? s.ExamStart;
                existing.IsTentative = s.IsTentative;
                existing.Note = s.Note ?? "";
                existing.SourceName = s.SourceName;
                existing.SourceUrl = s.SourceUrl;
            }

            // 科目資訊整份替換，讓 SeedData 為唯一真實來源
            db.ExamSubjects.RemoveRange(exam.Subjects);
            exam.Subjects = e.Subjects.Select((s, i) => new ExamSubjectInfo
            {
                Track = s.Track,
                SubjectName = s.SubjectName,
                Format = s.Format,
                SourceName = s.SourceName,
                SourceUrl = s.SourceUrl,
                SortOrder = i
            }).ToList();
        }
        await db.SaveChangesAsync(ct);
        return new(upserted, sessionsAdded, 0, 0, 0);
    }

    public async Task<ImportReport> ImportOfficialPapersAsync(CancellationToken ct = default)
    {
        var dir = Path.Combine(SeedDir, "papers");
        if (!Directory.Exists(dir)) return new(0, 0, 0, 0, 0);
        var examIds = await db.Exams.ToDictionaryAsync(e => e.Slug, e => e.Id, ct);
        var withheldSlugs = await WithheldSlugsAsync(ct);
        var existing = (await db.Papers.Select(p => new { p.ExamCode, p.PaperCode }).ToListAsync(ct))
            .Select(p => (p.ExamCode, p.PaperCode)).ToHashSet();
        var bulk = db.Database.IsSqlServer();
        int papers = 0, questions = 0, skipped = 0;
        var files = Directory.GetFiles(dir, "*.json").Concat(Directory.GetFiles(dir, "*.json.gz")).Order();
        foreach (var file in files)
        {
            List<PaperSeed> pack;
            await using (var fs = File.OpenRead(file))
            await using (Stream src = file.EndsWith(".gz") ? new System.IO.Compression.GZipStream(fs, System.IO.Compression.CompressionMode.Decompress) : fs)
                pack = (await JsonSerializer.DeserializeAsync<List<PaperSeed>>(src, Json, ct))!;
            var added = new List<(QuestionPaper Paper, List<Question> Questions)>();
            foreach (var p in pack)
            {
                if (!existing.Add((p.ExamCode, p.PaperCode)))
                {
                    skipped++;
                    continue;
                }
                var withheld = withheldSlugs.Contains(p.ExamSlug);
                var paper = new QuestionPaper
                {
                    ContentWithheld = withheld,
                    ExamCode = p.ExamCode,
                    PaperCode = p.PaperCode,
                    ExamId = examIds.TryGetValue(p.ExamSlug, out var id) ? id : null,
                    ExamTitle = p.ExamTitle,
                    RocYear = p.RocYear,
                    Categories = Truncate(string.Join("、", p.Categories), 2000),
                    SubjectName = Truncate(p.SubjectName, 200),
                    SourceQuestionUrl = p.SourceQuestionUrl,
                    SourceAnswerUrl = p.SourceAnswerUrl ?? "",
                    AnswerNote = Truncate(p.AnswerNote ?? "", 2000),
                    SourceNote = p.SourceNote ?? "",
                    OfficialQuestionCount = p.OfficialQuestionCount,
                    ExcludedJson = JsonSerializer.Serialize(p.Excluded),
                    ImportedAt = DateTime.UtcNow,
                };
                // 這份試卷適用的所有考試類別（主類別＋共用的其他類別）
                foreach (var slug in p.ExamSlugs.Prepend(p.ExamSlug).Distinct())
                    if (examIds.TryGetValue(slug, out var eid))
                        paper.ExamLinks.Add(new PaperExamLink { ExamId = eid });
                var qs = withheld ? new List<Question>() : p.Questions.Select(q => new Question
                {
                    Number = q.No,
                    Type = q.Type == "multi" ? QuestionType.Multiple : QuestionType.Single,
                    Passage = q.Passage,
                    Stem = q.Stem,
                    OptionA = q.Options["A"],
                    OptionB = q.Options["B"],
                    OptionC = q.Options["C"],
                    OptionD = q.Options["D"],
                    OptionE = q.Options.GetValueOrDefault("E"),
                    Answer = AnswerChecker.Normalize(q.Answer)
                }).Concat(p.Essays.Select(e => new Question
                {
                    Number = Question.EssayNumberBase + e.No,
                    Type = QuestionType.Essay,
                    Stem = e.Text,
                    Remark = e.Remark
                })).ToList();
                if (!bulk) paper.Questions = qs;
                db.Papers.Add(paper);
                added.Add((paper, qs));
                papers++;
                questions += qs.Count;
            }
            await db.SaveChangesAsync(ct);
            if (bulk && added.Count > 0)
                await BulkInsertQuestionsAsync(added, ct);
            db.ChangeTracker.Clear();
        }
        if (papers > 0) CatalogCache.Clear();
        log.LogInformation("官方試題匯入：新增 {Papers} 份、{Questions} 題，略過 {Skipped} 份既有試卷", papers, questions, skipped);
        return new(0, 0, papers, questions, skipped);
    }

    /// <summary>
    /// 公開模式下不提供內文的考試：設定檔列出的，加上所有非考選部辦理的考試（國營事業、證照等，
    /// 試題著作權屬主辦單位；考選部國家考試試題依著作權法第 9 條不受保護）。
    /// </summary>
    private async Task<HashSet<string>> WithheldSlugsAsync(CancellationToken ct)
    {
        if (!site.Value.PublicMode) return [];
        var other = await db.Exams.Where(e => e.OrganizerType != OrganizerType.NationalExam).Select(e => e.Slug).ToListAsync(ct);
        return site.Value.WithheldExamSlugs.Concat(other).ToHashSet();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];

    /// <summary>SQL Server 上用 SqlBulkCopy 寫入題目：題庫有數十萬題，逐筆 INSERT 太慢。</summary>
    private async Task BulkInsertQuestionsAsync(List<(QuestionPaper Paper, List<Question> Questions)> added, CancellationToken ct)
    {
        var t = new System.Data.DataTable();
        foreach (var (name, type) in new (string, Type)[] {
            ("PaperId", typeof(int)), ("Number", typeof(int)), ("Type", typeof(int)), ("Passage", typeof(string)), ("Stem", typeof(string)),
            ("OptionA", typeof(string)), ("OptionB", typeof(string)), ("OptionC", typeof(string)), ("OptionD", typeof(string)),
            ("OptionE", typeof(string)), ("Answer", typeof(string)), ("Remark", typeof(string)) })
            t.Columns.Add(name, type);
        foreach (var (paper, qs) in added)
            foreach (var q in qs)
                t.Rows.Add(paper.Id, q.Number, (int)q.Type, (object?)q.Passage ?? DBNull.Value, q.Stem, q.OptionA, q.OptionB, q.OptionC, q.OptionD,
                    (object?)q.OptionE ?? DBNull.Value, q.Answer, (object?)q.Remark ?? DBNull.Value);
        if (t.Rows.Count == 0) return;
        var conn = (Microsoft.Data.SqlClient.SqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);
        using var copy = new Microsoft.Data.SqlClient.SqlBulkCopy(conn) { DestinationTableName = "Questions", BatchSize = 5000, BulkCopyTimeout = 600 };
        foreach (System.Data.DataColumn c in t.Columns) copy.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await copy.WriteToServerAsync(t, ct);
    }

    /// <summary>
    /// 公開模式下，移除不提供內文之試卷的題目（以及相關作答、錯題、收藏、筆記、留言），
    /// 避免從非公開版本切換過來的資料庫仍保留內文。
    /// </summary>
    public async Task<int> PurgeWithheldContentAsync(CancellationToken ct = default)
    {
        if (!site.Value.PublicMode) return 0;
        var slugs = (await WithheldSlugsAsync(ct)).ToList();
        var papers = db.Papers.Where(p => p.Exam != null && slugs.Contains(p.Exam.Slug));
        var qids = db.Questions.Where(q => papers.Select(p => p.Id).Contains(q.PaperId)).Select(q => q.Id);
        if (!await qids.AnyAsync(ct))
        {
            await papers.Where(p => !p.ContentWithheld).ExecuteUpdateAsync(u => u.SetProperty(p => p.ContentWithheld, true), ct);
            return 0;
        }
        await db.PracticeAnswers.Where(a => qids.Contains(a.QuestionId)).ExecuteDeleteAsync(ct);
        await db.WrongQuestions.Where(a => qids.Contains(a.QuestionId)).ExecuteDeleteAsync(ct);
        await db.Bookmarks.Where(a => qids.Contains(a.QuestionId)).ExecuteDeleteAsync(ct);
        await db.QuestionNotes.Where(a => qids.Contains(a.QuestionId)).ExecuteDeleteAsync(ct);
        await db.Comments.Where(a => qids.Contains(a.QuestionId)).ExecuteDeleteAsync(ct);
        await db.ErrataReports.Where(a => qids.Contains(a.QuestionId)).ExecuteDeleteAsync(ct);
        await db.Explanations.Where(a => qids.Contains(a.QuestionId)).ExecuteDeleteAsync(ct);
        var n = await db.Questions.Where(q => papers.Select(p => p.Id).Contains(q.PaperId)).ExecuteDeleteAsync(ct);
        await papers.ExecuteUpdateAsync(u => u.SetProperty(p => p.ContentWithheld, true), ct);
        log.LogInformation("公開模式：移除 {Count} 題不提供內文的題目", n);
        return n;
    }

    // ---- seed file shapes ----
    public sealed class ExamSeedFile
    {
        public List<ExamSeed> Exams { get; set; } = new();
    }

    public sealed class ExamSeed
    {
        public string Slug { get; set; } = "";
        public string Name { get; set; } = "";
        public string ShortName { get; set; } = "";
        public string Organizer { get; set; } = "";
        public OrganizerType OrganizerType { get; set; }
        public string Summary { get; set; } = "";
        public string OfficialUrl { get; set; } = "";
        public int SortOrder { get; set; }
        public string Group { get; set; } = "";
        public List<SessionSeed> Sessions { get; set; } = new();
        public List<SubjectSeed> Subjects { get; set; } = new();
    }

    public sealed class SessionSeed
    {
        public string Title { get; set; } = "";
        public DateOnly? RegistrationStart { get; set; }
        public DateOnly? RegistrationEnd { get; set; }
        public DateOnly ExamStart { get; set; }
        public DateOnly? ExamEnd { get; set; }
        public bool IsTentative { get; set; }
        public string? Note { get; set; }
        public string SourceName { get; set; } = "";
        public string SourceUrl { get; set; } = "";
    }

    public sealed class SubjectSeed
    {
        public string Track { get; set; } = "";
        public string SubjectName { get; set; } = "";
        public string Format { get; set; } = "";
        public string SourceName { get; set; } = "";
        public string SourceUrl { get; set; } = "";
    }

    public sealed class PaperSeed
    {
        public string ExamCode { get; set; } = "";
        public string PaperCode { get; set; } = "";
        public string ExamSlug { get; set; } = "";
        public string ExamTitle { get; set; } = "";
        public int RocYear { get; set; }
        public List<string> Categories { get; set; } = new();
        public string SubjectName { get; set; } = "";
        public string SourceQuestionUrl { get; set; } = "";
        public string SourceAnswerUrl { get; set; } = "";
        public string? AnswerNote { get; set; }
        public string? SourceNote { get; set; }
        /// <summary>這份試卷也適用的其他考試類別（同一場考試多種特考共用的科目）。</summary>
        public List<string> ExamSlugs { get; set; } = new();
        public int OfficialQuestionCount { get; set; }
        public List<ExcludedSeed> Excluded { get; set; } = new();
        public List<QuestionSeed> Questions { get; set; } = new();
        public List<EssaySeed> Essays { get; set; } = new();
    }

    public sealed class EssaySeed
    {
        public int No { get; set; }
        public string Text { get; set; } = "";
        public string? Remark { get; set; }
    }

    public sealed class ExcludedSeed
    {
        public int No { get; set; }
        public string Reason { get; set; } = "";
    }

    public sealed class QuestionSeed
    {
        public int No { get; set; }
        public string Type { get; set; } = "single";
        public string? Passage { get; set; }
        public string Stem { get; set; } = "";
        public Dictionary<string, string> Options { get; set; } = new();
        public string Answer { get; set; } = "";
    }
}
