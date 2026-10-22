using System.Text.Json.Serialization;

namespace ExamPrep.Showcase.Services;

/// <summary>data/catalog.json：考試類別與題數統計。</summary>
public sealed class Catalog
{
    [JsonPropertyName("totals")] public Totals Totals { get; set; } = new();
    [JsonPropertyName("exams")] public List<ExamInfo> Exams { get; set; } = new();
}

public sealed class Totals
{
    [JsonPropertyName("papers")] public int Papers { get; set; }
    [JsonPropertyName("choice")] public int Choice { get; set; }
    [JsonPropertyName("essay")] public int Essay { get; set; }
}

public sealed class ExamInfo
{
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("short")] public string Short { get; set; } = "";
    [JsonPropertyName("group")] public string Group { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("papers")] public int Papers { get; set; }
    [JsonPropertyName("choice")] public int Choice { get; set; }
    [JsonPropertyName("essay")] public int Essay { get; set; }
    [JsonPropertyName("first")] public int First { get; set; }
    [JsonPropertyName("last")] public int Last { get; set; }
}

/// <summary>data/exams/&lt;slug&gt;.json 的一列：一份試卷。</summary>
public sealed class PaperRow
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("f")] public string File { get; set; } = "";
    [JsonPropertyName("y")] public int Year { get; set; }
    [JsonPropertyName("t")] public string ExamTitle { get; set; } = "";
    [JsonPropertyName("s")] public string Subject { get; set; } = "";
    [JsonPropertyName("c")] public List<string> Categories { get; set; } = new();
    [JsonPropertyName("n")] public int Choice { get; set; }
    [JsonPropertyName("e")] public int Essay { get; set; }
    [JsonPropertyName("o")] public int Official { get; set; }
}

/// <summary>data/papers/&lt;代碼&gt;.json 裡的一份試卷。</summary>
public sealed class Paper
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("y")] public int Year { get; set; }
    [JsonPropertyName("t")] public string ExamTitle { get; set; } = "";
    [JsonPropertyName("s")] public string Subject { get; set; } = "";
    [JsonPropertyName("c")] public List<string> Categories { get; set; } = new();
    [JsonPropertyName("u")] public string QuestionUrl { get; set; } = "";
    [JsonPropertyName("a")] public string AnswerUrl { get; set; } = "";
    [JsonPropertyName("note")] public string Note { get; set; } = "";
    [JsonPropertyName("an")] public string AnswerNote { get; set; } = "";
    [JsonPropertyName("o")] public int Official { get; set; }
    [JsonPropertyName("x")] public List<Excluded> Excluded { get; set; } = new();
    [JsonPropertyName("q")] public List<Question> Questions { get; set; } = new();
    [JsonPropertyName("es")] public List<Essay> Essays { get; set; } = new();
}

public sealed class Excluded
{
    [JsonPropertyName("no")] public int No { get; set; }
    [JsonPropertyName("reason")] public string Reason { get; set; } = "";
}

public sealed class Question
{
    [JsonPropertyName("n")] public int No { get; set; }
    [JsonPropertyName("m")] public bool Multi { get; set; }
    [JsonPropertyName("p")] public string? Passage { get; set; }
    [JsonPropertyName("t")] public string Stem { get; set; } = "";
    [JsonPropertyName("o")] public List<string> Options { get; set; } = new();
    [JsonPropertyName("a")] public string Answer { get; set; } = "";
}

public sealed class Essay
{
    [JsonPropertyName("n")] public int No { get; set; }
    [JsonPropertyName("t")] public string Text { get; set; } = "";
    [JsonPropertyName("r")] public string? Remark { get; set; }
}

/// <summary>錯題本裡的一題（存在瀏覽器 localStorage，連同題目內容一起存，離線也能複習）。</summary>
public sealed class WrongItem
{
    public string PaperId { get; set; } = "";
    public string PaperTitle { get; set; } = "";
    public string QuestionUrl { get; set; } = "";
    public Question Question { get; set; } = new();
    public string LastPick { get; set; } = "";
    public int Streak { get; set; }
    public DateTime AddedAt { get; set; }
}
