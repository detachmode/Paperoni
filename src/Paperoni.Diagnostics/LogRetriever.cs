using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Paperoni.Contract;

namespace Paperoni.Diagnostics;

internal sealed class LogRetriever(
    AlbumWorkingDirectory workingDirectory,
    IConfiguration configuration) : ILogRetriever
{
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
    private const string TimeOnlyFormat = "HH:mm:ss.fff";

    private static readonly Regex s_timestampRegex = new(
        @"(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})",
        RegexOptions.Compiled);

    public string GetLogContent(int albumId)
    {
        var logDir = configuration["LogPath"];
        if (string.IsNullOrEmpty(logDir))
        {
            logDir = workingDirectory.BasePath;
        }

        var logEntries = new List<(DateTime Timestamp, string Line)>();
        var traceEntries = new List<(DateTime Timestamp, string Line)>();

        // 1. Collect log lines from paperoni*.log files (max 30)
        foreach (var file in Directory.EnumerateFiles(logDir, "paperoni*.log").OrderByDescending(f => f))
        {
            foreach (var line in ReadLinesShared(file).Reverse())
            {
                if (!MatchesAlbum(line, albumId.ToString()))
                {
                    continue;
                }

                if (line.Contains("[DBG]", StringComparison.Ordinal))
                {
                    continue;
                }

                logEntries.Add(ParseAndReconstructLogLine(line));
                if (logEntries.Count >= 30)
                {
                    break;
                }
            }

            if (logEntries.Count >= 30)
            {
                break;
            }
        }

        // 2. Collect trace lines from traces.log (max 20)
        var tracePath = Path.Combine(workingDirectory.RequireWorkingDirectory(albumId), "traces.log");
        if (File.Exists(tracePath))
        {
            foreach (var line in ReadLinesShared(tracePath).Reverse().Take(20))
            {
                traceEntries.Add(ParseAndReconstructTraceLine(line));
            }
        }

        // 3. Merge and sort chronologically (oldest first)
        var allEntries = new List<(DateTime Timestamp, string Line)>(
            logEntries.Count + traceEntries.Count);
        allEntries.AddRange(logEntries);
        allEntries.AddRange(traceEntries);

        if (allEntries.Count == 0)
        {
            return string.Empty;
        }

        allEntries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        // 4. Build output with date header
        var firstDate = allEntries[0].Timestamp.ToString("yyyy-MM-dd");
        var lines = new List<string>(allEntries.Count + 1) { $"📋 Album {albumId} — {firstDate}" };
        lines.AddRange(allEntries.Select(e => e.Line));

        return string.Join("\n", lines);
    }

    private static bool MatchesAlbum(string line, string albumId)
    {
        return line.Contains("album " + albumId, StringComparison.OrdinalIgnoreCase) ||
               line.Contains("message " + albumId, StringComparison.OrdinalIgnoreCase) ||
               line.Contains("AlbumId=" + albumId, StringComparison.Ordinal) ||
               line.Contains(albumId + ".jpg", StringComparison.OrdinalIgnoreCase) ||
               line.Contains(albumId + ".pdf", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> ReadLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static (DateTime Timestamp, string Line) ParseAndReconstructLogLine(string line)
    {
        var match = s_timestampRegex.Match(line);
        if (!match.Success)
        {
            return (DateTime.MaxValue, line);
        }

        if (!DateTime.TryParseExact(match.Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var timestamp))
        {
            return (DateTime.MaxValue, line);
        }

        var ts = timestamp.ToString(TimeOnlyFormat);
        var rest = line[(match.Index + match.Length)..];

        string content;
        if (match.Index > 0 && line[match.Index - 1] == '[')
        {
            var trimmed = rest.TrimStart();
            content = trimmed.Length >= 2 ? trimmed[2..] : string.Empty;
        }
        else
        {
            var trimmed = rest.TrimStart();
            const int tzLength = 6;
            if (trimmed.Length > tzLength && trimmed[tzLength] == ' ')
            {
                content = trimmed[(tzLength + 1)..];
            }
            else if (trimmed.Length >= tzLength)
            {
                content = trimmed[tzLength..];
            }
            else
            {
                content = trimmed;
            }
        }

        return (timestamp, $"{ts}  {content}");
    }

    private static (DateTime Timestamp, string Line) ParseAndReconstructTraceLine(string line)
    {
        var match = s_timestampRegex.Match(line);
        if (!match.Success)
        {
            return (DateTime.MaxValue, line);
        }

        if (!DateTime.TryParseExact(match.Value, TimestampFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var timestamp))
        {
            return (DateTime.MaxValue, line);
        }

        var ts = timestamp.ToString(TimeOnlyFormat);
        var rest = line[(match.Index + match.Length)..];

        return (timestamp, $"{ts}{rest}");
    }
}
