namespace ExamPrep.Web.Services;

/// <summary>以台灣時間計算「今天」，避免伺服器在 UTC 時倒數天數差一天。</summary>
public class TaipeiDate(TimeProvider clock)
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Taipei");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), Zone).DateTime);

    public int DaysUntil(DateOnly date) => date.DayNumber - Today.DayNumber;

    public static string Roc(DateOnly d) => $"{d.Year - 1911}/{d.Month}/{d.Day}";

    public static string RocWithWeekday(DateOnly d) =>
        $"{d.Year - 1911}/{d.Month}/{d.Day}（{"日一二三四五六"[(int)d.DayOfWeek]}）";

    public static DateTime ToTaipeiTime(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Zone);
}
