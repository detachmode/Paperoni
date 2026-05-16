using System.Threading.Channels;
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
    Channel<int> retryChannel,
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
            var albumTask = queue.Dequeue(stoppingToken);
            var retryTask = retryChannel.Reader.ReadAsync(stoppingToken).AsTask();

            try
            {
                var completed = await Task.WhenAny(albumTask, retryTask);

                int msgId;
                bool isRetry;

                if (completed == albumTask)
                {
                    var entry = await albumTask;
                    msgId = entry.Photos.First().Value.MessageId;
                    isRetry = false;
                    logger.LogInformation("Worker received album with {count} items: {items}", entry.Photos.Count,
                        string.Join(", ", entry.Photos.Keys.ToList()));
                }
                else
                {
                    msgId = await retryTask;
                    isRetry = true;
                    logger.LogInformation("Retrying album {MsgId}", msgId);
                }

                await ProcessAlbum(msgId, isRetry, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception e)
            {
                logger.LogError(e, "An error occured during processing album");
            }

            await Task.Delay(100, stoppingToken);
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
