using System.Diagnostics;
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
                using var activity = Tracer.StartActivity<AlbumProcessor>();
                activity?.SetTag("AlbumId", item.MessageId);
                activity?.SetTag("isRetry", item.IsRetry);

                using var _ = logger.BeginScope(new Dictionary<string, object> { ["AlbumId"] = item.MessageId });
                logger.ProcessingAlbum(item.MessageId, item.IsRetry);
                await ProcessAlbum(item.MessageId, item.IsRetry, stoppingToken);

                albumIdAccessor.Id = null;
                activity?.SetStatus(ActivityStatusCode.Ok);
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

    private async Task ProcessAlbum(int msgId, bool isRetry, CancellationToken stoppingToken)
    {
        try
        {
            if (isRetry)
            {
                var oldAiResult = await workingDirectory.GetData<AiResult>(msgId, stoppingToken);
                if (oldAiResult?.Title is not null)
                {
                    logger.CleaningOldFiles(msgId, oldAiResult.Title);
                    await markdownPublisher.DeletePreviousAsync(oldAiResult.Title, stoppingToken);
                    await pdfPublisher.DeletePreviousAsync(oldAiResult.Title, stoppingToken);
                }
            }

            logger.AiSummaryStarting(msgId);
            await telegram.EditReply(msgId, isRetry ? "🔄 Retrying ..." : "🤖 AI is reading it ..");
            await ai.CreateAiSummary(msgId, stoppingToken);
            logger.AiSummaryDone(msgId);

            logger.PdfCreationStarting(msgId);
            await telegram.EditReply(msgId, "📄 Creating PDF ..");
            await pdfCreator.CreatePdf(msgId, stoppingToken);
            logger.PdfCreationDone(msgId);

            logger.PublishingMarkdown(msgId);
            await markdownPublisher.PublishFileAsync(msgId, stoppingToken);
            logger.PublishingPdf(msgId);
            await pdfPublisher.PublishFileAsync(msgId, stoppingToken);

            await telegram.SetReaction(msgId, "👏");

            var testMode = bool.TryParse(configuration["TestMode"], out var tm) && tm;
            await telegram.EditReply(msgId,
                $"""
                 Done:
                 ✅ Created PDF
                 ✅ Published Markdown summary
                 ✅ Published PDF
                 {(testMode ? "🧪 Test mode" : "")}
                 """);
            logger.AlbumComplete(msgId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.AlbumProcessingError(e, msgId);
            await telegram.EditReply(msgId, "Failed to process: " + e.Message);
        }
    }
}
