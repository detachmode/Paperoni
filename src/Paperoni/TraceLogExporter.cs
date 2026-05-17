using System.Diagnostics;
using System.Linq;
using OpenTelemetry;

namespace Paperoni;

internal sealed class TraceLogExporter(string outputPath) : BaseExporter<Activity>
{
    private readonly string _filePath = Path.Combine(outputPath, "traces.log");

    public override ExportResult Export(in Batch<Activity> batch)
    {
        try
        {
            var spans = new List<Activity>((int)batch.Count);
            foreach (var item in batch)
                spans.Add(item);

            var lines = new List<string>();

            foreach (var traceGroup in spans.GroupBy(s => s.TraceId))
            {
                var ordered = traceGroup.OrderBy(s => s.StartTimeUtc).ToList();
                if (lines.Count > 0)
                    lines.Add("");

                var children = ordered
                    .GroupBy(s => s.ParentSpanId)
                    .ToDictionary(g => g.Key, g => g.ToList());

                var roots = ordered
                    .Where(s => s.ParentSpanId == default || !children.ContainsKey(s.ParentSpanId))
                    .ToList();

                foreach (var root in roots)
                {
                    WriteSpan(lines, root, "");
                    PrintTree(lines, root, children, "");
                }
            }

            File.AppendAllLines(_filePath, lines);
            return ExportResult.Success;
        }
        catch
        {
            return ExportResult.Failure;
        }
    }

    private static void PrintTree(List<string> lines, Activity parent,
        Dictionary<ActivitySpanId, List<Activity>> children, string prefix)
    {
        if (!children.TryGetValue(parent.SpanId, out var siblings))
            return;

        for (var i = 0; i < siblings.Count; i++)
        {
            var isLast = i == siblings.Count - 1;
            var connector = isLast ? "└── " : "├── ";
            var nextPrefix = prefix + (isLast ? "    " : "│   ");

            WriteSpan(lines, siblings[i], prefix + connector);
            PrintTree(lines, siblings[i], children, nextPrefix);
        }
    }

    private static void WriteSpan(List<string> lines, Activity activity, string treePrefix)
    {
        var ts = activity.StartTimeUtc.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var dur = activity.Duration.TotalMilliseconds;
        var status = activity.Status.ToString();
        var parentId = activity.ParentSpanId == default
            ? "       " : activity.ParentSpanId.ToString()[..6];
        var tags = string.Join(", ", activity.TagObjects
            .Where(t => t.Value is not null)
            .Select(t => $"{t.Key}={t.Value}"));

        lines.Add(
            $"{treePrefix}[{ts}] [{activity.TraceId.ToString()[..12]}] " +
            $"[{activity.SpanId.ToString()[..6]}] [{parentId}] " +
            $"{activity.DisplayName,-40} {dur,8:F1}ms {status,-8} {tags}");
    }
}
