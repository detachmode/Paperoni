using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Paperoni.Ai;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using Paperoni.ImageProcessing;
using Paperoni.Telegram;
using Paperoni.Telegram.Album;
using static Paperoni.Diagnostics.Diagnostics;

namespace Paperoni.AlbumProcessing;

internal class AlbumProcessor(
    AlbumQueue queue,
    IAiService ai,
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

                await Tracer.TraceAsync<AlbumProcessor>(async scope =>
                {
                    scope.SetTag("isRetry", item.IsRetry);

                    using var _ = logger.BeginScope(new Dictionary<string, object> { ["AlbumId"] = item.MessageId });
                    logger.ProcessingAlbum(item.IsRetry);
                    success = await ProcessAlbum(item.MessageId, item.IsRetry, stoppingToken);
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

    private async Task<bool> ProcessAlbum(int albumId, bool isRetry, CancellationToken stoppingToken)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (isRetry)
            {
                var traceDir = workingDirectory.RequireWorkingDirectory(albumId);
                var tracePath = Path.Combine(traceDir, "traces.log");
                if (File.Exists(tracePath))
                {
                    File.Delete(tracePath);
                }

                var oldAiResult = await workingDirectory.GetData<AiResult>(albumId, stoppingToken);
                if (oldAiResult?.Title is not null)
                {
                    logger.CleaningOldFiles(oldAiResult.Title);
                    await markdownPublisher.DeletePreviousAsync(oldAiResult.Title, stoppingToken);
                    await pdfPublisher.DeletePreviousAsync(oldAiResult.Title, stoppingToken);
                }
            }

            logger.AiSummaryStarting();
            await telegram.UpdateDashboard(albumId,
                isRetry ? "🔄 Retrying.." : "🤖 AI reading..", queue.PendingCount);
            await ai.CreateAiSummary(albumId, (type, desc) =>
            {
                switch (type)
                {
                    case DebugOutputType.Reasoning:
                        _ = telegram.UpdateDashboard(albumId, "🤖 AI thinking..", queue.PendingCount);
                        break;
                    case DebugOutputType.PartialOutput:
                        _ = telegram.UpdateDashboard(albumId, "🤖 AI is formulating the final output ..",
                            queue.PendingCount);
                        break;
                }
            }, stoppingToken);
            logger.AiSummaryDone();

            logger.PdfCreationStarting();
            await telegram.UpdateDashboard(albumId, "📄 Creating PDF ..", queue.PendingCount);
            await pdfCreator.CreatePdf(albumId, stoppingToken);
            logger.PdfCreationDone();

            logger.PublishingMarkdown();
            await telegram.UpdateDashboard(albumId, "📤 Publishing..", queue.PendingCount);
            await markdownPublisher.PublishFileAsync(albumId, stoppingToken);
            logger.PublishingPdf();
            await pdfPublisher.PublishFileAsync(albumId, stoppingToken);

            await telegram.SetReaction(albumId, "👏");

            sw.Stop();
            var duration = sw.Elapsed.TotalSeconds;
            var testMode = settings.TestMode;
            await telegram.EditReply(albumId,
                $"Done in {duration:F1}s — Paperoni v{VersionInfo.Version}{(testMode ? " 🧪" : "")}");
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
            await telegram.EditReply(albumId, "Failed to process: " + e.Message);
            await telegram.UpdateDashboard(albumId, $"❌ Failed: {e.Message}", queue.PendingCount);
            return false;
        }
    }
}
