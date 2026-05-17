using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Paperoni.Contract;

namespace Paperoni.Diagnostics;

internal sealed class LogRetriever(
    AlbumWorkingDirectory workingDirectory,
    IConfiguration configuration) : ILogRetriever
{
    private static readonly Regex s_timestampRegex = new(
        @"(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})",
        RegexOptions.Compiled);

    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";

    public string GetLogContent(int msgId)
    {
        var logDir = configuration["LogPath"];
        if (string.IsNullOrEmpty(logDir))
        {
            logDir = workingDirectory.BasePath;
        }

        var msgIdStr = msgId.ToString();
        var logEntries = new List<(DateTime Timestamp, string Line)>();
        var traceEntries = new List<(DateTime Timestamp, string Line)>();

        // 1. Collect log lines from paperoni*.log files (max 30)
        foreach (var file in Directory.EnumerateFiles(logDir, "paperoni*.log").OrderByDescending(f => f))
        {
            foreach (var line in File.ReadLines(file).Reverse())
            {
                if (line.Contains("album " + msgIdStr, StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("message " + msgIdStr, StringComparison.OrdinalIgnoreCase) ||
                    line.Contains(msgIdStr + ".jpg", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains(msgIdStr + ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    logEntries.Add(ParseAndReconstructLogLine(line));
                    if (logEntries.Count >= 30)
                    {
                        break;
                    }
                }
            }

            if (logEntries.Count >= 30)
            {
                break;
            }
        }

        // 2. Collect trace lines from traces.log (max 20)
        var tracePath = Path.Combine(workingDirectory.RequireWorkingDirectory(msgId), "traces.log");
        if (File.Exists(tracePath))
        {
            foreach (var line in File.ReadLines(tracePath).Reverse().Take(20))
            {
                traceEntries.Add(ParseTimestampOnly(line));
            }
        }

        // 3. Merge and sort chronologically (oldest first)
        var allEntries = new List<(DateTime Timestamp, string Line)>(
            logEntries.Count + traceEntries.Count);
        allEntries.AddRange(logEntries);
        allEntries.AddRange(traceEntries);
        allEntries.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

        return string.Join("\n", allEntries.Select(e => e.Line));
    }

    /// <summary>
    /// Parses a Serilog log line (Format A or B), extracts the timestamp,
    /// and reconstructs it in the unified format: "{timestamp}  {content}".
    /// </summary>
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

        var ts = match.Value;
        var rest = line[(match.Index + match.Length)..];

        string content;
        if (match.Index > 0 && line[match.Index - 1] == '[')
        {
            // Format A: [2026-05-17 20:39:57.547] [INF] [AlbumId=42] message
            // rest starts with "] ...", skip past "] "
            var trimmed = rest.TrimStart();
            content = trimmed.Length > 2 ? trimmed[2..] : trimmed;
        }
        else
        {
            // Format B: 2026-05-17 20:39:57.547 +02:00 [INF] message
            // rest starts with " +02:00 ...", skip past timezone offset
            var trimmed = rest.TrimStart();
            // Timezone offset is 6 chars: +/-HH:MM
            const int tzLength = 6;
            if (trimmed.Length > tzLength && trimmed[tzLength] == ' ')
            {
                content = trimmed[(tzLength + 1)..];
            }
            else
            {
                content = trimmed[tzLength..];
            }
        }

        return (timestamp, $"{ts}  {content}");
    }

    /// <summary>
    /// Parses a trace line's timestamp only; the line is already in the
    /// unified format and is returned as-is.
    /// </summary>
    private static (DateTime Timestamp, string Line) ParseTimestampOnly(string line)
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

        return (timestamp, line);
    }
}
