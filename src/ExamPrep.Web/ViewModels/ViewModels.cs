using System.ComponentModel.DataAnnotations;
using ExamPrep.Web.Models;

namespace ExamPrep.Web.ViewModels;

public class RegisterVm
{
    [Required(ErrorMessage = "請輸入暱稱"), StringLength(50), Display(Name = "暱稱")]
    public string DisplayName { get; set; } = "";

    [Required(ErrorMessage = "請輸入電子郵件"), EmailAddress(ErrorMessage = "電子郵件格式不正確"), Display(Name = "電子郵件")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "請輸入密碼"), StringLength(100, MinimumLength = 8, ErrorMessage = "密碼至少 8 個字元"), DataType(DataType.Password), Display(Name = "密碼")]
    public string Password { get; set; } = "";

    [Compare(nameof(Password), ErrorMessage = "兩次輸入的密碼不一致"), DataType(DataType.Password), Display(Name = "確認密碼")]
    public string ConfirmPassword { get; set; } = "";

    public string? ReturnUrl { get; set; }
}

public class LoginVm
{
    [Required(ErrorMessage = "請輸入電子郵件"), EmailAddress, Display(Name = "電子郵件")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "請輸入密碼"), DataType(DataType.Password), Display(Name = "密碼")]
    public string Password { get; set; } = "";

    [Display(Name = "記住我")]
    public bool RememberMe { get; set; } = true;

    public string? ReturnUrl { get; set; }
}

public record CountdownItem(ExamSession Session, int DaysToExam, int? DaysToRegistrationEnd, bool IsTarget);

public class DashboardVm
{
    public string DisplayName { get; set; } = "";
    public List<CountdownItem> Targets { get; set; } = new();
    public CountdownItem? NextExam { get; set; }
    public int TodayMinutes { get; set; }
    public int DailyGoalMinutes { get; set; }
    public int WeekMinutes { get; set; }
    public int StreakDays { get; set; }
    public int AnsweredCount { get; set; }
    public double? Accuracy { get; set; }
    public int OpenWrongCount { get; set; }
    public List<SubjectAccuracy> WeakSubjects { get; set; } = new();
    public List<PracticeSession> RecentSessions { get; set; } = new();
}

public record SubjectAccuracy(string Subject, int Answered, int Correct)
{
    public double Rate => Answered == 0 ? 0 : (double)Correct / Answered;
}

public record ExamStat(Exam Exam, int Papers, int Questions, int FirstYear, int LastYear);

public class LandingVm
{
    public List<ExamStat> ExamStats { get; set; } = new();
    public Question? Sample { get; set; }
    public int PaperCount { get; set; }
    public int QuestionCount { get; set; }
    public int ExamCount { get; set; }
    public List<CountdownItem> Upcoming { get; set; } = new();
}

public class CalendarVm
{
    public List<CountdownItem> Upcoming { get; set; } = new();
    public List<CountdownItem> Past { get; set; } = new();
    public string? Organizer { get; set; }
    public bool LoggedIn { get; set; }
}

public class PaperRow
{
    public int Id { get; set; }
    public int RocYear { get; set; }
    public string ExamTitle { get; set; } = "";
    public string SubjectName { get; set; } = "";
    public string Categories { get; set; } = "";
    public string PaperCode { get; set; } = "";
    public int QuestionCount { get; set; }
    public int OfficialCount { get; set; }
    public string? ExamSlug { get; set; }
    public int? BestScore { get; set; }
}

public class PracticeIndexVm
{
    public const int PageSize = 50;
    public int Page { get; set; } = 1;
    public int PageCount { get; set; } = 1;
    public int Total { get; set; }
    public string? Exam { get; set; }
    public int? Year { get; set; }
    public string? Q { get; set; }
    public List<Exam> Exams { get; set; } = new();
    public List<int> Years { get; set; } = new();
    public List<PaperRow> Papers { get; set; } = new();
    public List<(string Subject, int Count)> Subjects { get; set; } = new();
    public Dictionary<string, int> ExamPaperCounts { get; set; } = new();
    public int AllPaperCount { get; set; }
}

public class TakeVm
{
    public PracticeSession Session { get; set; } = null!;
    public List<Question> Questions { get; set; } = new();
    public Dictionary<int, PracticeAnswer> Answers { get; set; } = new();
    public HashSet<int> InWrongBook { get; set; } = new();
}

public class WrongBookVm
{
    public string? Subject { get; set; }
    public bool ShowMastered { get; set; }
    public List<(string Subject, int Open)> Subjects { get; set; } = new();
    public List<WrongQuestion> Items { get; set; } = new();
}

public class StudyVm
{
    public List<StudySubjectProgress> Subjects { get; set; } = new();
    public List<DayTotal> Last14Days { get; set; } = new();
    public List<StudyLog> RecentLogs { get; set; } = new();
    public int DailyGoalMinutes { get; set; }
    public DateOnly Today { get; set; }
    public LogInput NewLog { get; set; } = new();
}

public record StudySubjectProgress(StudySubject Subject, int TotalMinutes, int Last7Minutes)
{
    public double Percent => Subject.TargetHours <= 0 ? 0 : Math.Min(1, TotalMinutes / 60.0 / Subject.TargetHours);
}

public record DayTotal(DateOnly Date, int Minutes, List<(string Color, int Minutes)> Parts);

public class LogInput
{
    [Required(ErrorMessage = "請選擇科目")]
    public int? StudySubjectId { get; set; }

    [Required]
    public DateOnly Date { get; set; }

    [Range(1, 1440, ErrorMessage = "時數需介於 1～1440 分鐘")]
    public int Minutes { get; set; } = 60;

    [StringLength(300)]
    public string? Note { get; set; }
}

public class SubjectInput
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "請輸入科目名稱"), StringLength(100)]
    public string Name { get; set; } = "";

    [Range(0, 2000, ErrorMessage = "目標時數需介於 0～2000")]
    public int TargetHours { get; set; } = 60;

    [RegularExpression("^#[0-9a-fA-F]{6}$")]
    public string Color { get; set; } = "#2f6f5e";
}

public class CsvImportVm
{
    [Required(ErrorMessage = "請填寫考試名稱"), StringLength(200), Display(Name = "考試名稱")]
    public string ExamTitle { get; set; } = "";

    [Display(Name = "對應考試")]
    public int? ExamId { get; set; }

    [Range(80, 200, ErrorMessage = "請輸入民國年"), Display(Name = "年度（民國）")]
    public int RocYear { get; set; } = 115;

    [Required(ErrorMessage = "請填寫科目"), StringLength(200), Display(Name = "科目")]
    public string SubjectName { get; set; } = "";

    [StringLength(2000), Display(Name = "適用類科／職階")]
    public string? Categories { get; set; }

    [Required(ErrorMessage = "請填寫試卷代號"), StringLength(30), Display(Name = "試卷代號")]
    public string PaperCode { get; set; } = "";

    [Required(ErrorMessage = "請填寫題目來源網址"), Url(ErrorMessage = "網址格式不正確"), StringLength(500), Display(Name = "題目來源網址")]
    public string SourceUrl { get; set; } = "";

    [Url(ErrorMessage = "網址格式不正確"), StringLength(500), Display(Name = "答案來源網址")]
    public string? AnswerSourceUrl { get; set; }

    public List<Exam> Exams { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class SessionInput
{
    public int? Id { get; set; }

    [Required, Display(Name = "考試")]
    public int ExamId { get; set; }

    [Required(ErrorMessage = "請填寫名稱"), StringLength(100), Display(Name = "名稱")]
    public string Title { get; set; } = "";

    [Display(Name = "報名開始")] public DateOnly? RegistrationStart { get; set; }
    [Display(Name = "報名截止")] public DateOnly? RegistrationEnd { get; set; }
    [Required, Display(Name = "考試開始")] public DateOnly ExamStart { get; set; }
    [Display(Name = "考試結束")] public DateOnly? ExamEnd { get; set; }
    [Display(Name = "日期為暫定")] public bool IsTentative { get; set; }
    [StringLength(500), Display(Name = "備註")] public string? Note { get; set; }

    [Required(ErrorMessage = "請填寫來源名稱"), StringLength(100), Display(Name = "來源名稱")]
    public string SourceName { get; set; } = "";

    [Required(ErrorMessage = "請填寫來源網址"), Url(ErrorMessage = "網址格式不正確"), StringLength(500), Display(Name = "來源網址")]
    public string SourceUrl { get; set; } = "";
}
