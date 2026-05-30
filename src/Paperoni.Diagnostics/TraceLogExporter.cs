using System.Diagnostics;
using System.Globalization;
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
                var albumId = ResolveAlbumId(item);

                if (albumId is { } id)
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

        var detail = FormatDetails(activity);
        return $"{ts}  {statusEmoji} {activity.DisplayName} {dur:F0}ms{detail}";
    }

    private static string FormatDetails(Activity activity)
    {
        var details = new List<string>();
        AddTag(details, activity, "file");
        AddTag(details, activity, "croppingMode");
        AddTag(details, activity, "cropStrategy");
        AddTag(details, activity, "cropConfidence");
        AddTag(details, activity, "cropScore", value => value switch
        {
            double d => d.ToString("F2", CultureInfo.InvariantCulture),
            float f => f.ToString("F2", CultureInfo.InvariantCulture),
            _ => value.ToString()
        });

        return details.Count == 0 ? string.Empty : " — " + string.Join(", ", details);
    }

    private static void AddTag(List<string> details, Activity activity, string tag,
        Func<object, string?>? format = null)
    {
        var value = activity.GetTagItem(tag);
        if (value is null)
        {
            return;
        }

        var formatted = format?.Invoke(value) ?? value.ToString();
        if (!string.IsNullOrWhiteSpace(formatted))
        {
            details.Add($"{tag}={formatted}");
        }
    }

    private static int? ResolveAlbumId(Activity activity)
    {
        for (var current = activity; current is not null; current = current.Parent)
        {
            var value = current.GetTagItem("AlbumId");
            if (value is int id)
            {
                return id;
            }

            if (value is long longId && longId >= int.MinValue && longId <= int.MaxValue)
            {
                return (int)longId;
            }

            if (value is string text && int.TryParse(text, out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }
}
