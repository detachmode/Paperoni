using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Paperoni.Contract;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static Paperoni.Contract.Diagnostics;

namespace Paperoni.Telegram.Album;

internal sealed class TelegramPhotoAlbumCollector(
    ITelegramBotClient botClient,
    AlbumQueue queue,
    AlbumWorkingDirectory workingDirectory,
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
            return;

        if (query.Data is not { } data)
            return;

        await botClient.AnswerCallbackQuery(query.Id);

        if (data.StartsWith("retry:") && int.TryParse(data.AsSpan(6), out var retryId))
        {
            if (query.Message is { } message)
                await botClient.EditMessageText(message.Chat.Id, message.MessageId, "🔄 Retrying ...");

            queue.Enqueue(new WorkItem(retryId, true));
            logger.LogInformation("Retry requested for album {MsgId}", retryId);
        }
    }

    private async Task HandleMessage(Message message, UpdateType type)
    {
        if (message.Photo is not { Length: > 0 } photoSizes)
            return;

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

            var album = new Album
            {
                Photos =
                {
                    [message.MessageId] = file
                }
            };
            await DownloadAndEnqueue(album);
            return;
        }

        var key = new AlbumKey(message.Chat.Id, message.MediaGroupId);

        var buffer = _albums.GetOrAdd(key, _ => new Album());

        lock (buffer.SyncRoot)
        {
            buffer.Photos[message.MessageId] = file;
            logger.LogInformation("Photo {MsgId} added to media group {GroupId}", message.MessageId, message.MediaGroupId);

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
                return;

            lock (buffer.SyncRoot)
            {
                buffer.Timer?.Dispose();
            }

            if (buffer.Photos.Count == 0)
                return;

            var first = buffer.Photos.First().Value;
            logger.LogInformation("Flushing album {MsgId} with {Count} photos",
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

        var downloadFolder = workingDirectory.GetDownloadPath(first.MessageId);
        await SaveMetaData(album);

        await _semaphore.WaitAsync();
        try
        {
            var botReply = await botClient.SendMessage(chatId, $"⬇️ Downloading {album.Photos.Count} files ..",
                replyParameters: msgId);

            await SaveMetaData(album, botReply.MessageId);

            foreach (var albumValue in album.Photos.Values)
            {
                await DownloadFile(downloadFolder, albumValue.MessageId + ".jpg", albumValue.FileId);
            }

            queue.Enqueue(new WorkItem(first.MessageId, false));
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
            Date =first.Date,
            Caption = album.Photos.Values.Select(photo => photo.Caption).ToList(),
            MessageId = first.MessageId,
            ChatId = first.ChatId,
            ReplyMessageId = replyMessageId,
            AlbumMessageIds = album.Photos.Values.Select(photo => photo.MessageId).ToList(),
        };

        await workingDirectory.WriteData(first.MessageId,metaData);
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
