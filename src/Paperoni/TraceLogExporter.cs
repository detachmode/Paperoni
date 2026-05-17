using System.Diagnostics;
using OpenTelemetry;
using Paperoni.Contract;

namespace Paperoni;

internal sealed class TraceLogExporter(
    AlbumWorkingDirectory workingDirectory,
    string fallbackPath) : BaseExporter<Activity>
{
    public override ExportResult Export(in Batch<Activity> batch)
    {
        try
        {
            var byAlbumId = new Dictionary<int, List<string>>();
            var fallback = new List<string>();

            foreach (var item in batch)
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
                var path = Path.Combine(workingDirectory.GetDownloadPath(id), "traces.log");
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
        var ts = activity.StartTimeUtc.ToString("HH:mm:ss.fff");
        var status = activity.Status == ActivityStatusCode.Ok ? "OK" :
            activity.Status == ActivityStatusCode.Error ? "ER" : "UN";

        var dur = activity.Duration.TotalMilliseconds;

        var tags = string.Join(", ", activity.TagObjects
            .Where(t => t.Value is not null)
            .Select(t => $"{t.Key}={t.Value}"));

        return $"{ts}  {status,-3} {activity.DisplayName,-45} {dur,8:F0}ms  {{{tags}}}";
    }
}
