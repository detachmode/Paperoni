using Paperoni.Ai;

namespace Paperoni.Tests;

public class TitleAutoCorrectionTest
{
    [Theory]
    [InlineData("   title: 2024-05-02 Some Text", "2024-05-02 Some Text")]
    [InlineData("title:    NoDate Some Text", "2026-01-01 NoDate Some Text")]
    [InlineData("title: 2999-05-02 Some Text", "2026-01-01 Some Text")]
    public void TestTitleFixing(string line, string expected)
    {
        var now = DateTime.Parse("2026-01-01");
        var title = MarkdownHelper.GetTitleFromMarkdown(
            $"""
             ---
             {line}
             ---
             # Some Info
             """
            , now
        );
        Assert.Equal(title, expected);
    }
}
