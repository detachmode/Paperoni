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

    [Fact]
    public void GetTitleFromMarkdown_NoFrontmatter_FallsBackToGuid()
    {
        var now = DateTime.Parse("2026-01-01");
        var title = MarkdownHelper.GetTitleFromMarkdown(
            """
            # Just a heading
            Some content without any frontmatter.
            """,
            now
        );
        Assert.StartsWith("2026-01-01", title);
        var guidPart = title[11..];
        Assert.True(Guid.TryParse(guidPart, out _));
    }

    [Fact]
    public void GetTitleFromMarkdown_EmptyString_FallsBackToGuid()
    {
        var now = DateTime.Parse("2026-01-01");
        var title = MarkdownHelper.GetTitleFromMarkdown("", now);
        Assert.StartsWith("2026-01-01", title);
        var guidPart = title[11..];
        Assert.True(Guid.TryParse(guidPart, out _));
    }

    [Theory]
    [InlineData("title: a/b:c", "a_b:c")]
    [InlineData("title: 2024-05-02 file/name", "2024-05-02 file_name")]
    public void GetTitleFromMarkdown_InvalidFilenameChars_AreReplaced(string line, string expected)
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
        Assert.Equal(expected, title);
    }
}
