namespace ExamPrep.Web.Services;

/// <summary>作答比對：單選比對一個字母，複選需選項完全相同（不計順序、大小寫、空白）。</summary>
public static class AnswerChecker
{
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var letters = raw.ToUpperInvariant()
            .Where(c => c is >= 'A' and <= 'E')
            .Distinct()
            .OrderBy(c => c);
        return new string(letters.ToArray());
    }

    public static bool IsCorrect(string? selected, string answer)
    {
        var s = Normalize(selected);
        return s.Length > 0 && s == Normalize(answer);
    }
}
