using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Paperoni.Ai;
using Paperoni.Contract;
using Paperoni.ImageProcessing;
using Paperoni.Telegram;
using Paperoni.Telegram.Album;

namespace Paperoni.AlbumProcessing;

internal class AlbumProcessor(
    AlbumQueue queue,
    IAiService ai,
    [FromKeyedServices(PublisherTarget.Markdown)] IFilePublisher markdownPublisher,
    [FromKeyedServices(PublisherTarget.Pdf)] IFilePublisher pdfPublisher,
    IPdfCreator pdfCreator,
    ITelegramReplier telegram,
    AlbumWorkingDirectory workingDirectory,
    ILogger<AlbumProcessor> logger,
    IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var item = await queue.Dequeue(stoppingToken);
                logger.LogInformation("Worker received album {MsgId} (retry: {IsRetry})",
                    item.MessageId, item.IsRetry);
                await ProcessAlbum(item.MessageId, item.IsRetry, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "An error occured during processing album");
            }
        }
    }

    private async Task ProcessAlbum(int msgId, bool isRetry, CancellationToken stoppingToken)
    {
        try
        {
            string? oldTitle = null;
            if (isRetry)
            {
                var oldAiResult = await workingDirectory.GetData<AiResult>(msgId, stoppingToken);
                oldTitle = oldAiResult?.Title;
            }

            await telegram.EditReply(msgId, isRetry ? "🔄 Retrying ..." : "🤖 AI is reading it ..");
            await ai.CreateAiSummary(msgId, stoppingToken);

            await telegram.EditReply(msgId, "📄 Creating PDF ..");
            await pdfCreator.CreatePdf(msgId, stoppingToken);

            await markdownPublisher.PublishFileAsync(msgId, stoppingToken);
            await pdfPublisher.PublishFileAsync(msgId, stoppingToken);

            if (oldTitle is not null)
            {
                await markdownPublisher.DeletePreviousAsync(oldTitle, stoppingToken);
                await pdfPublisher.DeletePreviousAsync(oldTitle, stoppingToken);
            }

            var testMode = bool.TryParse(configuration["TestMode"], out var tm) && tm;
            await telegram.EditReply(msgId,
                $"""
                 Done:
                 ✅ Created PDF
                 ✅ Published Markdown summary
                 ✅ Published PDF
                 {(testMode ? "🧪 Test mode" : "")}
                 """);
        }
        catch (Exception e)
        {
            logger.LogError(e, "An error occured during processing album");
            await telegram.EditReply(msgId, "Failed to process: " + e.Message);
        }
    }
}
