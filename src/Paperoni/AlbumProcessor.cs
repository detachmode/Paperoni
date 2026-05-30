using System.Diagnostics;
using System.Text.Json;
using Paperoni.Ai;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using Paperoni.ImageProcessing;
using Paperoni.Telegram;

namespace Paperoni;

internal sealed class AlbumProcessor(
    AlbumQueue queue,
    IPipelineService pipeline,
    [FromKeyedServices(PublisherTarget.Markdown)]
    IFilePublisher markdownPublisher,
    [FromKeyedServices(PublisherTarget.Pdf)]
    IFilePublisher pdfPublisher,
    IPdfCreator pdfCreator,
    ITelegramReplier telegram,
    WorkingDirectory workingDirectory,
    AlbumIdAccessor albumIdAccessor,
    ILogger<AlbumProcessor> logger,
    AlbumProcessingSettings settings) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            WorkItem? item = null;
            bool success = false;
            try
            {
                item = await queue.Dequeue(stoppingToken);

                albumIdAccessor.Id = item.MessageId;

                await ActivityExtensions.Tracer.TraceAsync<AlbumProcessor>(async scope =>
                {
                    scope.SetTag("isRetry", item.IsRetry);
                    scope.SetTag("forceLlmCrop", item.ForceLlmCrop);

                    using var _ = logger.BeginScope(new Dictionary<string, object> { ["AlbumId"] = item.MessageId });
                    logger.ProcessingAlbum(item.IsRetry);
                    success = await ProcessAlbum(item.MessageId, item.IsRetry, item.ForceLlmCrop, stoppingToken);
                });

                albumIdAccessor.Id = null;
            }
            catch (OperationCanceledException)
            {
                albumIdAccessor.Id = null;
                break;
            }
            catch (Exception e)
            {
                albumIdAccessor.Id = null;
                logger.AlbumProcessingError(e, item?.MessageId);
            }

            if (success && queue.PendingCount == 0)
            {
                await telegram.DeleteDashboard();
            }
        }
    }

    private async Task<bool> ProcessAlbum(int albumId, bool isRetry, bool forceLlmCrop, CancellationToken stoppingToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (isRetry)
            {
                var retryMetaData = await workingDirectory.GetData<MetaData>(albumId, stoppingToken);
                if (retryMetaData is null)
                {
                    var errorMessage = $"Unknown AlbumId {albumId} for retry.";
                    await telegram.ReplyError(albumId, errorMessage);
                    await telegram.UpdateDashboard(albumId, $"❌ Failed: {errorMessage}", queue.PendingCount);
                    return false;
                }

                var traceDir = workingDirectory.RequireWorkingDirectory(albumId);
                var tracePath = Path.Combine(traceDir, "traces.log");
                if (File.Exists(tracePath))
                {
                    File.Delete(tracePath);
                }

                var oldResult = await workingDirectory.GetData<PipelineResult>(albumId, stoppingToken);
                if (oldResult?.Filename is not null)
                {
                    logger.CleaningOldFiles(oldResult.Filename);
                    await markdownPublisher.DeletePreviousAsync(oldResult.Filename, ".md", stoppingToken);
                    await pdfPublisher.DeletePreviousAsync(oldResult.Filename, ".pdf", stoppingToken);
                }
            }

            logger.AiSummaryStarting();
            await telegram.UpdateDashboard(albumId, "🤖 AI is reading ..", queue.PendingCount);
            var result = await pipeline.RunAsync(albumId,
                (_, desc) => { telegram.UpdateDashboard(albumId, desc, queue.PendingCount).ConfigureAwait(false); },
                stoppingToken);
            logger.AiSummaryDone();

            logger.PdfCreationStarting();
            await telegram.UpdateDashboard(albumId, "📄 Creating PDF ..", queue.PendingCount);
            await pdfCreator.CreatePdf(albumId,
                status => telegram.UpdateDashboard(albumId, status, queue.PendingCount),
                forceLlmCrop,
                stoppingToken);
            logger.PdfCreationDone();

            logger.PublishingMarkdown();
            await telegram.UpdateDashboard(albumId, $"📤 Publishing..", queue.PendingCount);
            await markdownPublisher.PublishStringAsync(result.FormattedContent, result.Filename, stoppingToken);
            logger.PublishingPdf();

            var workingDir = workingDirectory.RequireWorkingDirectory(albumId);
            var pdfPath = Path.Combine(workingDir, $"{result.Filename}.pdf");
            await pdfPublisher.PublishFileAsync(pdfPath, result.Filename, stoppingToken);

            await telegram.SetReaction(albumId, "👏");

            sw.Stop();
            var duration = sw.Elapsed.TotalSeconds;
            var testMode = settings.TestMode;
            await telegram.EditReply(albumId, BuildFinalStatus(albumId, result.Filename, duration, testMode));
            logger.AlbumComplete();
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.AlbumProcessingError(e, albumId);
            await telegram.ReplyError(albumId, e.Message);
            await telegram.UpdateDashboard(albumId, $"❌ Failed: {e.Message}", queue.PendingCount);
            return false;
        }
    }

    private string BuildFinalStatus(int albumId, string filename, double duration, bool testMode)
    {
        var lines = new List<string>
        {
            $"✅ Done in {FormatDuration(TimeSpan.FromSeconds(duration))} — Paperoni v{VersionInfo.Version}{(testMode ? " 🧪" : "")}",
            "",
            filename
        };

        var cropLines = GetCropSummary(albumId);
        if (cropLines.Count > 0)
        {
            lines.Add("");
            lines.Add("Crop:");
            lines.AddRange(cropLines);
        }

        lines.Add("");
        lines.Add("Files: Markdown ✅  PDF ✅");

        return string.Join("\n", lines);
    }

    private List<string> GetCropSummary(int albumId)
    {
        var workingDir = workingDirectory.RequireWorkingDirectory(albumId);
        var files = Directory.GetFiles(workingDir, "*.cropDecision.json")
            .OrderBy(Path.GetFileName)
            .ToList();

        var lines = new List<string>();
        var index = 1;
        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                var strategy = root.GetProperty("finalStrategy").GetString() ?? "Unknown";
                var reason = root.GetProperty("reason").GetString() ?? "";
                var openCv = root.GetProperty("openCv");
                var confidence = openCv.GetProperty("confidence").GetString() ?? "Unknown";
                var score = openCv.GetProperty("score").GetDouble();

                var status = strategy switch
                {
                    "OpenCv" => $"🟢 {index} OpenCV {confidence.ToLowerInvariant()} {score:F2} kept",
                    "Llm" => $"🟢 {index} LLM crop used",
                    _ => $"⚠️ {index} NoCrop ({Shorten(reason, 60)})"
                };
                lines.Add(status);
            }
            catch
            {
                lines.Add($"⚠️ {index} crop details unavailable");
            }

            index++;
        }

        return lines;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        }

        return $"{duration.TotalSeconds:F1}s";
    }

    private static string Shorten(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength] + "...";
    }
}
