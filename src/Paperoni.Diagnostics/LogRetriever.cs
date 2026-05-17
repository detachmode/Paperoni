using Microsoft.Extensions.Configuration;
using Paperoni.Contract;

namespace Paperoni.Diagnostics;

internal sealed class LogRetriever(
    AlbumWorkingDirectory workingDirectory,
    IConfiguration configuration) : ILogRetriever
{
    public string GetLogContent(int msgId)
    {
        var logDir = configuration["LogPath"];
        if (string.IsNullOrEmpty(logDir))
        {
            logDir = workingDirectory.BasePath;
        }

        var msgIdStr = msgId.ToString();
        var matchingLines = new List<string>();

        foreach (var file in Directory.EnumerateFiles(logDir, "paperoni*.log").OrderByDescending(f => f))
        {
            foreach (var line in File.ReadLines(file).Reverse())
            {
                if (line.Contains("album " + msgIdStr, StringComparison.OrdinalIgnoreCase) || line.Contains("message " + msgIdStr, StringComparison.OrdinalIgnoreCase)
                                                       || line.Contains(msgIdStr + ".jpg", StringComparison.OrdinalIgnoreCase) ||
                                                       line.Contains(msgIdStr + ".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    matchingLines.Add(line);
                    if (matchingLines.Count >= 30)
                    {
                        break;
                    }
                }
            }

            if (matchingLines.Count >= 30)
            {
                break;
            }
        }

        var tracePath = Path.Combine(workingDirectory.RequireWorkingDirectory(msgId), "traces.log");
        if (File.Exists(tracePath))
        {
            var traceLines = File.ReadLines(tracePath).Reverse().Take(20).ToList();
            if (traceLines.Count > 0)
            {
                matchingLines.Insert(0, "");
                matchingLines.Insert(0, "--- Traces ---");
                matchingLines.InsertRange(1, traceLines);
            }
        }

        return string.Join("\n", matchingLines);
    }
}
