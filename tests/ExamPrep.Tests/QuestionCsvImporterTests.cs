using System.Text;
using ExamPrep.Web.Models;
using ExamPrep.Web.Services;

namespace ExamPrep.Tests;

public class QuestionCsvImporterTests
{
    private static QuestionCsvImporter.Result Parse(string csv)
        => new QuestionCsvImporter().Parse(new MemoryStream(Encoding.UTF8.GetBytes(csv)));

    private const string Header = "題號,題型,題組文章,題幹,A,B,C,D,E,答案\n";

    [Fact]
    public void Valid_rows_are_parsed_and_sorted()
    {
        var r = Parse(Header +
                      "2,複選,,題幹二,甲,乙,丙,丁,戊,eb\n" +
                      "1,單選,,\"含逗號，與\"\"引號\"\"的題幹\",甲,乙,丙,丁,,C\n");
        Assert.True(r.Ok, string.Join(";", r.Errors));
        Assert.Equal([1, 2], r.Questions.Select(q => q.Number));
        Assert.Equal("含逗號，與\"引號\"的題幹", r.Questions[0].Stem);
        Assert.Equal(QuestionType.Multiple, r.Questions[1].Type);
        Assert.Equal("BE", r.Questions[1].Answer);
        Assert.Null(r.Questions[0].OptionE);
    }

    [Fact]
    public void Invalid_rows_are_all_reported_and_nothing_is_ok()
    {
        var r = Parse(Header +
                      "1,單選,,題幹,a,b,c,d,,AB\n" +   // 單選多答案
                      "1,單選,,題幹,a,b,c,d,,E\n" +    // 題號重複、答案 E 無選項
                      "x,問答,,,a,,c,d,,Q\n");         // 題號、題型、題幹、選項、答案皆錯
        Assert.False(r.Ok);
        Assert.Contains(r.Errors, e => e.Contains("單選題只能有一個答案"));
        Assert.Contains(r.Errors, e => e.Contains("重複"));
        Assert.Contains(r.Errors, e => e.Contains("沒有 E 選項"));
        Assert.Contains(r.Errors, e => e.Contains("不是正整數"));
    }

    [Fact]
    public void Missing_required_header_is_rejected()
    {
        var r = Parse("題號,題幹,A,B,C,D\n1,x,a,b,c,d\n");
        Assert.False(r.Ok);
        Assert.Contains("缺少欄位", r.Errors.Single());
    }
}
