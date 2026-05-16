using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Paperoni.Ai;
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
    ILogger<AlbumProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var entry = await queue.Dequeue(stoppingToken);
            logger.LogInformation("Worker received album with {count} items: {items}", entry.Photos.Count,
                string.Join(", ", entry.Photos.Keys.ToList()));

            var first = entry.Photos.First().Value;
            var msgId = first.MessageId;
            try
            {
                await telegram.EditReply(msgId, "🤖 AI is reading it ..");
                await ai.CreateAiSummary(msgId, stoppingToken);

                await telegram.EditReply(msgId, "📄 Creating PDF ..");
                await pdfCreator.CreatePdf(msgId, stoppingToken);

                await markdownPublisher.PublishFileAsync(msgId, stoppingToken);
                await pdfPublisher.PublishFileAsync(msgId, stoppingToken);

                await telegram.EditReply(msgId,
                    $"""
                     Done:
                     ✅ Created PDF
                     ✅ Published AI summary to Obsidian 
                     ✅ Published PDF
                     """);
            }
            catch (Exception e)
            {
                logger.LogError(e, "An error occured during processing album");
                await telegram.EditReply(msgId, "Failed to process: " + e.Message);
            }

            await Task.Delay(100, stoppingToken);
        }
    }
}
