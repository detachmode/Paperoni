namespace Paperoni.Ai;

public static class MarkdownHelper
{
    /// <summary>
    /// Invalid characters for obsidian Markdown files includes:  * " \ / <![CDATA[<]]> > : | ?
    /// </summary>
    private static readonly HashSet<char> s_invalidFilenameChars =
    [
        '\0', '<', '>', ':', '"', '/', '\\', '|', '?', '*',
        .. Enumerable.Range(1, 31).Select(i => (char)i)
    ];

    public static string FixMarkdownFromAi(string markdown, DateTime? now = null)
    {
        var nowFormatted = (now ?? DateTime.Now).ToString("yyyy-MM-dd");
        return markdown
            .Replace("DATE-UNKNOWN", nowFormatted, StringComparison.InvariantCultureIgnoreCase)
            .Replace("DATUM-UNKNOWN", nowFormatted, StringComparison.InvariantCultureIgnoreCase)
            .Replace("DATE-UNBEKANNT", nowFormatted, StringComparison.InvariantCultureIgnoreCase)
            .Replace("DATUM-UNBEKANNT", nowFormatted, StringComparison.InvariantCultureIgnoreCase);
    }

    public static string GetTitleFromMarkdown(string markdown, DateTime? now = null)
    {
        var lines = markdown.Split(Environment.NewLine).ToList();
        var title = lines.FirstOrDefault(x => x.Contains("title:")) ?? Guid.NewGuid().ToString();
        title = title.Replace("title:", "").Trim();
        title = AutoFixDate(title, now ?? DateTime.Now);

        title = string.Create(title.Length, title, static (span, t) =>
        {
            t.AsSpan().CopyTo(span);
            for (var i = 0; i < span.Length; i++)
            {
                if (s_invalidFilenameChars.Contains(span[i]))
                {
                    span[i] = '_';
                }
            }
        });
        return title;
    }

    private static string AutoFixDate(string title, DateTime now)
    {
        try
        {
            var substringDate = title.Substring(0, 10);
            var substringTitle = title.Substring(10, title.Length - 10).Trim();
            if (DateTime.TryParse(substringDate, out var dateForTitle))
            {
                if (dateForTitle > now)
                {
                    return $"{now:yyyy-MM-dd} {substringTitle}";
                }

                return title;
            }

            return $"{now:yyyy-MM-dd} {title.Trim()}";
        }
        catch (Exception)
        {
            return title;
        }
    }
}
