using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using ExamPrep.Web.Models;

namespace ExamPrep.Web.Services;

/// <summary>
/// 讀取使用者自行整理的題目 CSV。欄位：題號,題型,題組文章,題幹,A,B,C,D,E,答案
/// 每列皆檢查；任何一列有誤就整份不匯入，避免半套資料進資料庫。
/// </summary>
public class QuestionCsvImporter
{
    public sealed record Result(List<Question> Questions, List<string> Errors)
    {
        public bool Ok => Errors.Count == 0 && Questions.Count > 0;
    }

    private sealed class Row
    {
        public string 題號 { get; set; } = "";
        public string 題型 { get; set; } = "";
        public string? 題組文章 { get; set; }
        public string 題幹 { get; set; } = "";
        public string A { get; set; } = "";
        public string B { get; set; } = "";
        public string C { get; set; } = "";
        public string D { get; set; } = "";
        public string? E { get; set; }
        public string 答案 { get; set; } = "";
    }

    public static readonly string[] Headers = ["題號", "題型", "題組文章", "題幹", "A", "B", "C", "D", "E", "答案"];

    public Result Parse(Stream stream)
    {
        var errors = new List<string>();
        var list = new List<Question>();
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            TrimOptions = TrimOptions.Trim,
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = ctx => errors.Add($"第 {ctx.Context?.Parser?.Row} 列格式錯誤（引號未閉合？）"),
        };
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
        using var csv = new CsvReader(reader, config);
        try
        {
            csv.Read();
            csv.ReadHeader();
            var missing = Headers.Where(h => h != "題組文章" && h != "E" && !csv.HeaderRecord!.Contains(h)).ToList();
            if (missing.Count > 0)
                return new Result(list, [$"缺少欄位：{string.Join("、", missing)}"]);

            var seen = new HashSet<int>();
            while (csv.Read())
            {
                var line = csv.Parser.Row;
                var r = csv.GetRecord<Row>()!;
                if (!int.TryParse(r.題號, out var no) || no <= 0)
                {
                    errors.Add($"第 {line} 列：題號「{r.題號}」不是正整數");
                    continue;
                }
                if (!seen.Add(no)) errors.Add($"第 {line} 列：題號 {no} 重複");

                var type = r.題型 switch
                {
                    "單選" or "single" or "S" => QuestionType.Single,
                    "複選" or "multiple" or "M" => QuestionType.Multiple,
                    _ => (QuestionType?)null
                };
                if (type is null) errors.Add($"第 {line} 列：題型需為「單選」或「複選」");
                if (string.IsNullOrWhiteSpace(r.題幹)) errors.Add($"第 {line} 列：題幹空白");
                if (new[] { r.A, r.B, r.C, r.D }.Any(string.IsNullOrWhiteSpace))
                    errors.Add($"第 {line} 列：A～D 選項不可空白");

                var answer = AnswerChecker.Normalize(r.答案);
                var hasE = !string.IsNullOrWhiteSpace(r.E);
                if (answer.Length == 0 || answer.Length != r.答案.Count(char.IsLetter))
                    errors.Add($"第 {line} 列：答案「{r.答案}」需為 A～E 的字母");
                else if (answer.Contains('E') && !hasE)
                    errors.Add($"第 {line} 列：答案含 E 但沒有 E 選項");
                if (type == QuestionType.Single && answer.Length > 1)
                    errors.Add($"第 {line} 列：單選題只能有一個答案");

                list.Add(new Question
                {
                    Number = no,
                    Type = type ?? QuestionType.Single,
                    Passage = string.IsNullOrWhiteSpace(r.題組文章) ? null : r.題組文章,
                    Stem = r.題幹,
                    OptionA = r.A, OptionB = r.B, OptionC = r.C, OptionD = r.D,
                    OptionE = hasE ? r.E : null,
                    Answer = answer
                });
            }
        }
        catch (Exception ex) when (ex is CsvHelperException or HeaderValidationException)
        {
            errors.Add("CSV 無法解析：" + ex.Message.Split('\n')[0]);
        }
        return new Result(list.OrderBy(q => q.Number).ToList(), errors);
    }
}
