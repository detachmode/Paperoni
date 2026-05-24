namespace Paperoni.Ai;

public static class MarkdownHelper
{
    private static readonly HashSet<char> s_invalidFilenameChars =
    [
        '\0', '<', '>', ':', '"', '/', '\\', '|', '?', '*',
        .. Enumerable.Range(1, 31).Select(i => (char)i)
    ];

    public static string SanitizeFilename(string title)
    {
        return string.Create(title.Length, title, static (span, t) =>
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
    }

    public static string AutoFixDate(string title, DateTime? now = null)
    {
        var currentDate = now ?? DateTime.Now;
        try
        {
            var substringDate = title.Substring(0, Math.Min(10, title.Length));
            var substringTitle = title.Length > 10 ? title[10..].Trim() : title.Trim();
            if (DateTime.TryParse(substringDate, out var dateForTitle))
            {
                if (dateForTitle > currentDate)
                {
                    return $"{currentDate:yyyy-MM-dd} {substringTitle}";
                }

                return title;
            }

            return $"{currentDate:yyyy-MM-dd} {title.Trim()}";
        }
        catch (Exception)
        {
            return title;
        }
    }

    public static string FixMarkdownFromAi(string markdown, DateTime? now = null)
    {
        var nowFormatted = (now ?? DateTime.Now).ToString("yyyy-MM-dd");
        return markdown
            .Replace("DATE-UNKNOWN", nowFormatted, StringComparison.InvariantCultureIgnoreCase)
            .Replace("DATUM-UNKNOWN", nowFormatted, StringComparison.InvariantCultureIgnoreCase)
            .Replace("DATE-UNBEKANNT", nowFormatted, StringComparison.InvariantCultureIgnoreCase)
            .Replace("DATUM-UNBEKANNT", nowFormatted, StringComparison.InvariantCultureIgnoreCase);
    }
}