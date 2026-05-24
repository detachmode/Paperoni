using Paperoni.Ai;

namespace Paperoni.Tests;

public class TitleAutoCorrectionTest
{
    [Theory]
    [InlineData("2024-05-02 Some Text", "2024-05-02 Some Text")]
    [InlineData("NoDate Some Text", "2026-01-01 NoDate Some Text")]
    [InlineData("2999-05-02 Some Text", "2026-01-01 Some Text")]
    public void AutoFixDate_CorrectsFutureDates(string title, string expected)
    {
        var now = DateTime.Parse("2026-01-01");
        var result = MarkdownHelper.AutoFixDate(title, now);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("a>b<c", "a_b_c")]
    [InlineData("a?b*c", "a_b_c")]
    [InlineData("a:b|c", "a_b_c")]
    [InlineData("2024-05-02 file/name", "2024-05-02 file_name")]
    public void SanitizeFilename_ReplacesInvalidChars(string input, string expected)
    {
        var result = MarkdownHelper.SanitizeFilename(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void FixMarkdownFromAi_ReplacesDatePlaceholders()
    {
        var now = DateTime.Parse("2026-01-01");
        var result = MarkdownHelper.FixMarkdownFromAi("Due by DATE-UNKNOWN", now);
        Assert.Contains("2026-01-01", result);
    }
}