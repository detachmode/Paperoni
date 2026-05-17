namespace Paperoni.Ai;

public static class MarkdownHelper
{
    public static string FixMarkdownFromAi(string markdown, DateTime? now = null)
    {
        var nowFormatted = (now ?? DateTime.Now).ToString("yyyy-MM-dd");
        return markdown
            .Replace("DATE-UNKNOWN", nowFormatted)
            .Replace("DATUM-UNKNOWN", nowFormatted)
            .Replace("DATE-UNBEKANNT", nowFormatted)
            .Replace("DATUM-UNBEKANNT", nowFormatted);
    }

    public static string GetTitleFromMarkdown(string markdown, DateTime? now = null)
    {
        var lines = markdown.Split(Environment.NewLine).ToList();
        var title = lines.FirstOrDefault(x => x.Contains("title:")) ?? Guid.NewGuid().ToString();
        title = title.Replace("title:", "").Trim();
        title = AutoFixDate(title, now ?? DateTime.Now);

        Path.GetInvalidFileNameChars().ToList().ForEach(c => title = title.Replace(c, '_'));
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