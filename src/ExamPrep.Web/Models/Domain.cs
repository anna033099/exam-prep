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
    StateEnterprise = 1
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

    [MaxLength(500)]
    public string Categories { get; set; } = "";

    [MaxLength(200)]
    public string SubjectName { get; set; } = "";

    [MaxLength(500)]
    public string SourceQuestionUrl { get; set; } = "";

    [MaxLength(500)]
    public string SourceAnswerUrl { get; set; } = "";

    [MaxLength(2000)]
    public string AnswerNote { get; set; } = "";

    public int OfficialQuestionCount { get; set; }

    /// <summary>因含圖表等原因未收錄的題號與原因（JSON）。</summary>
    public string ExcludedJson { get; set; } = "[]";

    public DateTime ImportedAt { get; set; }

    public List<Question> Questions { get; set; } = new();
}

public enum QuestionType
{
    Single = 0,
    Multiple = 1
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
}
