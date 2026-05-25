using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Paperoni.Telegram.Album;

internal sealed class TelegramPhotoAlbumCollector(
    ITelegramBotClient botClient,
    AlbumQueue queue,
    WorkingDirectory workingDirectory,
    ITelegramReplier telegram,
    ILogger<TelegramPhotoAlbumCollector> logger) : IHostedService
{
    private readonly ConcurrentDictionary<AlbumKey, Album> _albums = new();
    private readonly TimeSpan _debounceTime = TimeSpan.FromSeconds(2);
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var me = await botClient.GetMe(cancellationToken: cancellationToken);
        if (botClient is TelegramBotClient tgBot)
        {
            tgBot.OnMessage += HandleMessage;
            tgBot.OnUpdate += HandleUpdate;
        }

        logger.LogInformation("@Started Telegram bot {Username} and listening for messages", me.Username);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stopping telegram bot message handler");
        if (botClient is TelegramBotClient tgBot)
        {
            tgBot.OnMessage -= HandleMessage;
            tgBot.OnUpdate -= HandleUpdate;
        }

        return Task.CompletedTask;
    }

    private async Task HandleUpdate(Update update)
    {
        if (update.CallbackQuery is not { } query)
        {
            return;
        }

        if (query.Data is not { } data)
        {
            logger.LogWarning("Callback query has no data");
            return;
        }

        logger.LogInformation("Callback received: {Data}", data);

        await botClient.AnswerCallbackQuery(query.Id);

        if (data.StartsWith("retry:") && int.TryParse(data.AsSpan(6), out var retryId))
        {
            if (query.Message is { } message)
            {
                await botClient.EditMessageText(message.Chat.Id, message.MessageId, $"🔄 Retrying album {message.MessageId} ..");
                await EnsureRetryMetadata(retryId, message);
            }

            queue.Enqueue(new WorkItem(retryId, true));
            logger.LogInformation("Retry requested for album {AlbumId}", retryId);
        }
        else if (data == "close_diag")
        {
            if (query.Message is { } diagMsg)
            {
                await botClient.DeleteMessage(diagMsg.Chat.Id, diagMsg.MessageId);
            }
        }
        else if (data.StartsWith("logs:") && int.TryParse(data.AsSpan(5), out var logId))
        {
            logger.LogInformation("Logs requested for album {AlbumId}", logId);
            try
            {
                await telegram.ShowDiagnostic(logId);
                logger.LogInformation("Diagnostic shown for album {AlbumId}", logId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to show diagnostic for album {AlbumId}", logId);
            }
        }
        else
        {
            logger.LogWarning("Unknown callback data: {Data}", data);
        }
    }

    private async Task EnsureRetryMetadata(int albumId, Message replyMessage)
    {
        var metadata = await workingDirectory.GetData<MetaData>(albumId) ?? new MetaData
        {
            MessageId = albumId,
            Date = replyMessage.Date,
            AlbumMessageIds = [albumId]
        };

        metadata.ChatId = replyMessage.Chat.Id;
        metadata.ReplyMessageId = replyMessage.MessageId;

        if (metadata.MessageId == 0)
        {
            metadata.MessageId = albumId;
        }

        if (metadata.AlbumMessageIds.Count == 0)
        {
            metadata.AlbumMessageIds.Add(albumId);
        }

        await workingDirectory.WriteData(albumId, metadata);
    }

    private async Task HandleCommand(Message message, string text)
    {
        if (text == "/version")
        {
            await botClient.SendMessage(message.Chat.Id, CommandResponses.Version());
        }
        else if (text == "/help")
        {
            await botClient.SendMessage(message.Chat.Id, CommandResponses.Help());
        }
    }

    private async Task HandleMessage(Message message, UpdateType type)
    {
        if (message.Text is { } text && text.StartsWith('/'))
        {
            await HandleCommand(message, text);
            return;
        }

        if (message.Photo is not { Length: > 0 } photoSizes)
        {
            return;
        }

        var bestPhoto = photoSizes[^1];

        var file = new TelegramPhotoFile(
            ChatId: message.Chat.Id,
            MessageId: message.MessageId,
            FileId: bestPhoto.FileId,
            FileUniqueId: bestPhoto.FileUniqueId,
            Caption: message.Caption,
            Date: message.Date
        );

        if (string.IsNullOrEmpty(message.MediaGroupId))
        {
            logger.LogInformation("Photo {MsgId} received (single)", message.MessageId);

            var album = new Album { Photos = { [message.MessageId] = file } };
            await DownloadAndEnqueue(album);
            return;
        }

        var key = new AlbumKey(message.Chat.Id, message.MediaGroupId);

        var buffer = _albums.GetOrAdd(key, _ => new Album());

        lock (buffer.SyncRoot)
        {
            buffer.Photos[message.MessageId] = file;
            logger.LogInformation("Photo {MsgId} added to media group {GroupId}", message.MessageId,
                message.MediaGroupId);

            buffer.Timer?.Dispose();

            buffer.Timer = new Timer(
#pragma warning disable CS4014
                callback: _ => Flush(key),
#pragma warning restore CS4014
                state: null,
                dueTime: _debounceTime,
                period: Timeout.InfiniteTimeSpan
            );
        }
    }

    private async Task Flush(AlbumKey key)
    {
        try
        {
            if (!_albums.TryRemove(key, out var buffer))
            {
                return;
            }

            lock (buffer.SyncRoot)
            {
                buffer.Timer?.Dispose();
            }

            if (buffer.Photos.Count == 0)
            {
                return;
            }

            var first = buffer.Photos.First().Value;
            logger.LogInformation("Flushing album {AlbumId} with {Count} photos",
                first.MessageId, buffer.Photos.Count);

            await DownloadAndEnqueue(buffer);
        }
        catch (Exception e)
        {
            logger.DownloadError(e);
        }
    }

    private async Task DownloadAndEnqueue(Album album)
    {
        var first = album.Photos.First().Value;
        var msgId = first.MessageId;
        var chatId = first.ChatId;

        using var logScope = logger.BeginScope(new Dictionary<string, object> { ["AlbumId"] = msgId });

        var downloadFolder = workingDirectory.RequireWorkingDirectory(msgId);
        await SaveMetaData(album);

        await DownloadAlbumFiles(album, downloadFolder);

        var markup = new InlineKeyboardMarkup([
            [
                InlineKeyboardButton.WithCallbackData("🔄 Retry", $"retry:{msgId}"),
                InlineKeyboardButton.WithCallbackData("📋 Logs", $"logs:{msgId}")
            ]
        ]);

        var botReply = await botClient.SendMessage(chatId,
            $"Downloaded album ({msgId}) with {album.Photos.Count} files — queued for processing ⏳️",
            replyParameters: msgId,
            replyMarkup: markup);

        await SaveMetaData(album, botReply.MessageId);

        queue.Enqueue(new WorkItem(msgId, false));
    }

    private async Task DownloadAlbumFiles(Album album, string downloadFolder)
    {
        await _semaphore.WaitAsync();
        try
        {
            var albumId = album.Photos.First().Value.MessageId;
            var totalBytes = 0L;
            foreach (var albumValue in album.Photos.Values)
            {
                totalBytes += await DownloadFile(downloadFolder, albumValue.MessageId + ".jpg", albumValue.FileId);
            }

            logger.AlbumDownloaded(album.Photos.Count, albumId, totalBytes);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task SaveMetaData(Album album, int? replyMessageId = null)
    {
        var first = album.Photos.First().Value;
        var metaData = new MetaData
        {
            Date = first.Date,
            Caption = album.Photos.Values.Select(photo => photo.Caption).ToList(),
            MessageId = first.MessageId,
            ChatId = first.ChatId,
            ReplyMessageId = replyMessageId,
            AlbumMessageIds = album.Photos.Values.Select(photo => photo.MessageId).ToList(),
        };

        await workingDirectory.WriteData(first.MessageId, metaData);
    }

    private async Task<long> DownloadFile(string path, string name, string fileId)
    {
        var filePath = Path.Combine(path, name);
        await using (var stream = File.Create(filePath))
        {
            var tgFile = await botClient.GetFile(fileId);
            await botClient.DownloadFile(tgFile, stream);
        }

        var size = new FileInfo(filePath).Length;
        logger.FileDownloaded(name, size);
        return size;
    }
}
