using Microsoft.Extensions.Configuration;
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
    AlbumWorkingDirectory workingDirectory,
    AlbumIdAccessor albumIdAccessor,
    ILogger<AlbumProcessor> logger,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            WorkItem? item = null;
            try
            {
                item = await queue.Dequeue(stoppingToken);

                albumIdAccessor.Id = item.MessageId;

                await Tracer.TraceAsync<AlbumProcessor>(async scope =>
                {
                    scope.SetTag("isRetry", item.IsRetry);

                    using var _ = logger.BeginScope(new Dictionary<string, object> { ["AlbumId"] = item.MessageId });
                    logger.ProcessingAlbum(item.IsRetry);
                    await ProcessAlbum(item.MessageId, item.IsRetry, stoppingToken);
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
        }
    }

    private async Task ProcessAlbum(int albumId, bool isRetry, CancellationToken stoppingToken)
    {
        try
        {
            if (isRetry)
            {
                var oldAiResult = await workingDirectory.GetData<AiResult>(albumId, stoppingToken);
                if (oldAiResult?.Title is not null)
                {
                    logger.CleaningOldFiles(oldAiResult.Title);
                    await markdownPublisher.DeletePreviousAsync(oldAiResult.Title, stoppingToken);
                    await pdfPublisher.DeletePreviousAsync(oldAiResult.Title, stoppingToken);
                }
            }

            logger.AiSummaryStarting();
            await telegram.EditReply(albumId, isRetry ? "🔄 Retrying ..." : "🤖 AI is reading it ..");
            await ai.CreateAiSummary(albumId, stoppingToken);
            logger.AiSummaryDone();

            logger.PdfCreationStarting();
            await telegram.EditReply(albumId, "📄 Creating PDF ..");
            await pdfCreator.CreatePdf(albumId, stoppingToken);
            logger.PdfCreationDone();

            logger.PublishingMarkdown();
            await markdownPublisher.PublishFileAsync(albumId, stoppingToken);
            logger.PublishingPdf();
            await pdfPublisher.PublishFileAsync(albumId, stoppingToken);

            await telegram.SetReaction(albumId, "👏");

            var testMode = bool.TryParse(configuration["TestMode"], out var tm) && tm;
            await telegram.EditReply(albumId,
                $"""
                 Done:
                 ✅ Created PDF
                 ✅ Published Markdown summary
                 ✅ Published PDF
                 {(testMode ? "🧪 Test mode" : "")}
                 """);
            logger.AlbumComplete();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.AlbumProcessingError(e, albumId);
            await telegram.EditReply(albumId, "Failed to process: " + e.Message);
        }
    }
}
