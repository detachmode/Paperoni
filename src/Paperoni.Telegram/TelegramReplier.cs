using Paperoni.Contract;
using Paperoni.Telegram.Album;
using Telegram.Bot;

namespace Paperoni.Telegram;

public interface ITelegramReplier
{
    Task EditReply(int msgId, string text);
}

public class TelegramReplier(ITelegramBotClient bot, AlbumWorkingDirectory workingDirectory) : ITelegramReplier
{
    public async Task EditReply(int msgId, string text)
    {
        var metadata = await workingDirectory.RequireData<MetaData>(msgId);
        var chatId = metadata.ChatId;
        var replyMessageId = metadata.ReplyMessageId;
        ArgumentNullException.ThrowIfNull(replyMessageId);

        await EditMessage(chatId, replyMessageId.Value, text);
    }

    private async Task EditMessage(long chatId, int msgId, string text)
    {
        await bot.EditMessageText(chatId, msgId, text);
    }
}