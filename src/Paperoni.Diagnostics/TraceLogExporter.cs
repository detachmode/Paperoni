using System.Diagnostics;
using OpenTelemetry;
using Paperoni.Contract;

namespace Paperoni.Diagnostics;

public sealed class TraceLogExporter(
    WorkingDirectory workingDirectory,
    string fallbackPath) : BaseExporter<Activity>
{
    public override ExportResult Export(in Batch<Activity> batch)
    {
        try
        {
            var activities = new List<Activity>();
            foreach (var item in batch)
            {
                activities.Add(item);
            }

            activities.Sort((a, b) => a.StartTimeUtc.CompareTo(b.StartTimeUtc));

            var byAlbumId = new Dictionary<int, List<string>>();
            var fallback = new List<string>();

            foreach (var item in activities)
            {
                var line = FormatSpan(item);
                var albumId = item.GetTagItem("AlbumId");

                if (albumId is int id)
                {
                    if (!byAlbumId.TryGetValue(id, out var lines))
                    {
                        lines = new List<string>();
                        byAlbumId[id] = lines;
                    }

                    lines.Add(line);
                }
                else
                {
                    fallback.Add(line);
                }
            }

            foreach (var (id, lines) in byAlbumId)
            {
                var path = Path.Combine(workingDirectory.RequireWorkingDirectory(id), "traces.log");
                File.AppendAllLines(path, lines);
            }

            if (fallback.Count > 0)
            {
                File.AppendAllLines(Path.Combine(fallbackPath, "traces.log"), fallback);
            }

            return ExportResult.Success;
        }
        catch
        {
            return ExportResult.Failure;
        }
    }

    private static string FormatSpan(Activity activity)
    {
        var ts = activity.StartTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff");
        var statusEmoji = activity.Status == ActivityStatusCode.Ok ? "✅" :
            activity.Status == ActivityStatusCode.Error ? "❌" : "⏳";

        var dur = activity.Duration.TotalMilliseconds;

        return $"{ts}  {statusEmoji} {activity.DisplayName} {dur:F0}ms";
    }
}
