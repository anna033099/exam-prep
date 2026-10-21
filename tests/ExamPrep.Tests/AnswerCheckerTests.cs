using ExamPrep.Web.Services;

namespace ExamPrep.Tests;

public class AnswerCheckerTests
{
    [Theory]
    [InlineData("c", "C")]
    [InlineData(" d b ", "BD")]
    [InlineData("EDB", "BDE")]
    [InlineData("AAB", "AB")]
    [InlineData("x1", "")]
    [InlineData(null, "")]
    public void Normalize_sorts_dedupes_and_drops_non_letters(string? raw, string expected)
        => Assert.Equal(expected, AnswerChecker.Normalize(raw));

    [Fact]
    public void Single_choice_requires_exact_letter()
    {
        Assert.True(AnswerChecker.IsCorrect("C", "C"));
        Assert.False(AnswerChecker.IsCorrect("A", "C"));
        Assert.False(AnswerChecker.IsCorrect("", "C"));
    }

    [Fact]
    public void Multiple_choice_requires_full_set_regardless_of_order()
    {
        Assert.True(AnswerChecker.IsCorrect("DB", "BD"));
        Assert.False(AnswerChecker.IsCorrect("B", "BD"));
        Assert.False(AnswerChecker.IsCorrect("BDE", "BD"));
    }
}
