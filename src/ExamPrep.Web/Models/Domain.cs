using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace ExamPrep.Web.Models;

public class ApplicationUser : IdentityUser
{
    [MaxLength(50)]
    public string DisplayName { get; set; } = "";
}

/// <summary>主辦單位類型：國家考試（考選部）或國營／公營事業招考。</summary>
public enum OrganizerType
{
    NationalExam = 0,
    StateEnterprise = 1,
    /// <summary>證照考試（技術士、金融、保險等）。</summary>
    Certification = 2,
    /// <summary>升學考試（學測、會考、統測等）。</summary>
    Admission = 3,
    /// <summary>其他招考（教師甄試、農漁會等）。</summary>
    Recruitment = 4
}

/// <summary>一種考試，例如「公務人員初等考試」「中華郵政職階人員甄試」。</summary>
public class Exam
{
    public int Id { get; set; }

    [MaxLength(20)]
    public string Slug { get; set; } = "";

    [MaxLength(100)]
    public string Name { get; set; } = "";

    [MaxLength(30)]
    public string ShortName { get; set; } = "";

    [MaxLength(50)]
    public string Organizer { get; set; } = "";

    public OrganizerType OrganizerType { get; set; }

    [MaxLength(1000)]
    public string Summary { get; set; } = "";

    [MaxLength(300)]
    public string OfficialUrl { get; set; } = "";

    public int SortOrder { get; set; }

    /// <summary>首頁與題庫的分類群組，例如「綜合國家考試」「警察・消防・海巡」。</summary>
    [MaxLength(30)]
    public string Group { get; set; } = "";

    public List<ExamSession> Sessions { get; set; } = new();
    public List<ExamSubjectInfo> Subjects { get; set; } = new();
}

/// <summary>某一年度的考期。日期一律來自官方或可追溯的公告；未公告者不建立。</summary>
public class ExamSession
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }

    [MaxLength(100)]
    public string Title { get; set; } = "";

    public DateOnly? RegistrationStart { get; set; }
    public DateOnly? RegistrationEnd { get; set; }
    public DateOnly ExamStart { get; set; }
    public DateOnly ExamEnd { get; set; }

    /// <summary>是否為「預定／暫定」日期。</summary>
    public bool IsTentative { get; set; }

    [MaxLength(500)]
    public string Note { get; set; } = "";

    [MaxLength(100)]
    public string SourceName { get; set; } = "";

    [MaxLength(500)]
    public string SourceUrl { get; set; } = "";
}

/// <summary>考試的應試科目（依類科或職階）。</summary>
public class ExamSubjectInfo
{
    public int Id { get; set; }
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }

    [MaxLength(100)]
    public string Track { get; set; } = "";

    [MaxLength(200)]
    public string SubjectName { get; set; } = "";

    [MaxLength(50)]
    public string Format { get; set; } = "";

    [MaxLength(100)]
    public string SourceName { get; set; } = "";

    [MaxLength(500)]
    public string SourceUrl { get; set; } = "";

    public int SortOrder { get; set; }
}

/// <summary>一份試卷（某次考試的某一科目）。</summary>
public class QuestionPaper
{
    public int Id { get; set; }

    /// <summary>考選部考試代碼（如 115010）或自訂代碼。</summary>
    [MaxLength(30)]
    public string ExamCode { get; set; } = "";

    /// <summary>試題代號（試卷右上角），同一代號視為同一份試卷。</summary>
    [MaxLength(30)]
    public string PaperCode { get; set; } = "";

    public int? ExamId { get; set; }
    public Exam? Exam { get; set; }

    [MaxLength(200)]
    public string ExamTitle { get; set; } = "";

    /// <summary>民國年。</summary>
    public int RocYear { get; set; }

    [MaxLength(2000)]
    public string Categories { get; set; } = "";

    [MaxLength(200)]
    public string SubjectName { get; set; } = "";

    [MaxLength(500)]
    public string SourceQuestionUrl { get; set; } = "";

    [MaxLength(500)]
    public string SourceAnswerUrl { get; set; } = "";

    [MaxLength(2000)]
    public string AnswerNote { get; set; } = "";

    /// <summary>來源說明，例如原公告單位與轉載網站。考選部試題留空。</summary>
    [MaxLength(500)]
    public string SourceNote { get; set; } = "";

    /// <summary>公開版不提供題目內文（著作權考量），只列試卷與原始來源連結。</summary>
    public bool ContentWithheld { get; set; }

    public int OfficialQuestionCount { get; set; }

    /// <summary>因含圖表等原因未收錄的題號與原因（JSON）。</summary>
    public string ExcludedJson { get; set; } = "[]";

    public DateTime ImportedAt { get; set; }

    public List<Question> Questions { get; set; } = new();

    /// <summary>同一份試卷也屬於其他考試類別時（例如同一場考試多種特考共用的國文），在這裡連結。</summary>
    public List<PaperExamLink> ExamLinks { get; set; } = new();
}

/// <summary>試卷與考試類別的多對多連結（包含 ExamId 本身）。</summary>
public class PaperExamLink
{
    public int PaperId { get; set; }
    public QuestionPaper? Paper { get; set; }
    public int ExamId { get; set; }
    public Exam? Exam { get; set; }
}

public enum QuestionType
{
    Single = 0,
    Multiple = 1,
    /// <summary>申論題：只供閱讀、筆記與討論，沒有官方標準答案，不進入刷題計分。</summary>
    Essay = 2
}

public class Question
{
    public int Id { get; set; }
    public int PaperId { get; set; }
    public QuestionPaper? Paper { get; set; }

    public int Number { get; set; }
    public QuestionType Type { get; set; }

    public string? Passage { get; set; }
    public string Stem { get; set; } = "";
    public string OptionA { get; set; } = "";
    public string OptionB { get; set; } = "";
    public string OptionC { get; set; } = "";
    public string OptionD { get; set; } = "";
    public string? OptionE { get; set; }

    /// <summary>標準答案，單選如 "C"，複選如 "BDE"（依字母排序）。</summary>
    [MaxLength(5)]
    public string Answer { get; set; } = "";

    /// <summary>提醒文字，例如「本題含圖表，請對照原卷」。</summary>
    [MaxLength(200)]
    public string? Remark { get; set; }

    public bool IsEssay => Type == QuestionType.Essay;

    /// <summary>申論題的題號從 1001 起存放，避免與同卷測驗題（1、2、3…）衝突。</summary>
    public const int EssayNumberBase = 1000;

    private static readonly string[] Cn = ["", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十"];

    /// <summary>畫面上的題號：測驗題「第 3 題」、申論題「申論第二題」。</summary>
    public string Label => IsEssay ? $"申論第{CnNumber(Number - EssayNumberBase)}題" : $"第 {Number} 題";

    public static string CnNumber(int n) => n switch
    {
        <= 10 and > 0 => Cn[n],
        < 20 => "十" + Cn[n - 10],
        < 100 => Cn[n / 10] + "十" + Cn[n % 10],
        _ => n.ToString()
    };

    public IEnumerable<(char Letter, string Text)> Options()
    {
        yield return ('A', OptionA);
        yield return ('B', OptionB);
        yield return ('C', OptionC);
        yield return ('D', OptionD);
        if (!string.IsNullOrWhiteSpace(OptionE)) yield return ('E', OptionE!);
    }
}

public class PracticeSession
{
    public int Id { get; set; }

    [MaxLength(450)]
    public string UserId { get; set; } = "";

    [MaxLength(200)]
    public string Title { get; set; } = "";

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int TotalCount { get; set; }
    public int CorrectCount { get; set; }

    /// <summary>整卷練習時對應的試卷；隨機抽題與錯題重練為 null。</summary>
    public int? PaperId { get; set; }

    public PracticeMode Mode { get; set; }

    /// <summary>逐題即時對答：每答一題就顯示對錯。</summary>
    public bool Instant { get; set; }

    /// <summary>計時模擬考的時限（分鐘）；null 表示不計時。</summary>
    public int? TimeLimitMinutes { get; set; }

    /// <summary>出題順序（題目 Id，以逗號分隔），讓作答頁與結果頁一致。</summary>
    public string QuestionIds { get; set; } = "";

    public List<PracticeAnswer> Answers { get; set; } = new();
}

public class PracticeAnswer
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public PracticeSession? Session { get; set; }
    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    [MaxLength(5)]
    public string Selected { get; set; } = "";

    public bool IsCorrect { get; set; }

    public DateTime AnsweredAt { get; set; }
}

public enum PracticeMode
{
    Paper = 0,
    Random = 1,
    WrongBook = 2,
    Custom = 3,
    Review = 4,
    Daily = 5
}

/// <summary>錯題本：每位使用者每題一筆。</summary>
public class WrongQuestion
{
    public int Id { get; set; }

    [MaxLength(450)]
    public string UserId { get; set; } = "";

    public int QuestionId { get; set; }
    public Question? Question { get; set; }

    public int WrongCount { get; set; }
    public int CorrectStreak { get; set; }
    public DateTime LastWrongAt { get; set; }
    public bool Mastered { get; set; }

    /// <summary>間隔複習：下次該複習的時間與目前間隔（天）。</summary>
    public DateTime? DueAt { get; set; }
    public int IntervalDays { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }
}

/// <summary>使用者關注的考期（首頁倒數）。</summary>
public class UserTargetExam
{
    public int Id { get; set; }

    [MaxLength(450)]
    public string UserId { get; set; } = "";

    public int ExamSessionId { get; set; }
    public ExamSession? ExamSession { get; set; }
}

public class StudySubject
{
    public int Id { get; set; }

    [MaxLength(450)]
    public string UserId { get; set; } = "";

    [Required, MaxLength(100)]
    public string Name { get; set; } = "";

    /// <summary>目標總時數（小時）。</summary>
    [Range(0, 2000)]
    public int TargetHours { get; set; }

    [MaxLength(7)]
    public string Color { get; set; } = "#2f6f5e";

    public bool Archived { get; set; }

    public List<StudyLog> Logs { get; set; } = new();
}

public class StudyLog
{
    public int Id { get; set; }

    [MaxLength(450)]
    public string UserId { get; set; } = "";

    public int StudySubjectId { get; set; }
    public StudySubject? StudySubject { get; set; }

    public DateOnly Date { get; set; }

    [Range(1, 1440)]
    public int Minutes { get; set; }

    [MaxLength(300)]
    public string? Note { get; set; }
}

public class UserSetting
{
    [Key, MaxLength(450)]
    public string UserId { get; set; } = "";

    /// <summary>每日讀書目標（分鐘）。</summary>
    public int DailyGoalMinutes { get; set; } = 180;

    /// <summary>每日刷題目標（題）。</summary>
    public int DailyGoalQuestions { get; set; } = 20;

    /// <summary>是否以暱稱出現在排行榜（預設不公開）。</summary>
    public bool ShowOnLeaderboard { get; set; }
}

public class QuestionBookmark
{
    public int Id { get; set; }
    [MaxLength(450)] public string UserId { get; set; } = "";
    public int QuestionId { get; set; }
    public Question? Question { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>使用者對單一題目的個人筆記。</summary>
public class QuestionNote
{
    public int Id { get; set; }
    [MaxLength(450)] public string UserId { get; set; } = "";
    public int QuestionId { get; set; }
    public Question? Question { get; set; }
    [MaxLength(2000)] public string Body { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}

public class QuestionComment
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question? Question { get; set; }
    [MaxLength(450)] public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    [MaxLength(1000)] public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int ReportCount { get; set; }
    public bool IsHidden { get; set; }
}

public enum ErrataKind
{
    StemText = 0,
    OptionText = 1,
    Answer = 2,
    AiExplanation = 3,
    Other = 4
}

public enum ErrataStatus
{
    Open = 0,
    Resolved = 1,
    Rejected = 2
}

public class ErrataReport
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question? Question { get; set; }
    [MaxLength(450)] public string UserId { get; set; } = "";
    public ErrataKind Kind { get; set; }
    [MaxLength(1000)] public string Detail { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public ErrataStatus Status { get; set; }
    [MaxLength(500)] public string? AdminNote { get; set; }
}

/// <summary>AI 生成的詳解（快取，每題一份），一律標示為 AI 生成。</summary>
public class QuestionExplanation
{
    public int Id { get; set; }
    public int QuestionId { get; set; }
    public Question? Question { get; set; }
    [MaxLength(100)] public string Model { get; set; } = "";
    public string Body { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    [MaxLength(450)] public string RequestedByUserId { get; set; } = "";
}
