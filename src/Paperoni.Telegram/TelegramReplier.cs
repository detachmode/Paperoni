using Paperoni.Contract;
using Paperoni.Telegram.Album;
using Telegram.Bot;
using Telegram.Bot.Types.ReplyMarkups;

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

        var markup = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("🔄 Retry", $"retry:{msgId}")]
        ]);

        await bot.EditMessageText(chatId, replyMessageId.Value, text, replyMarkup: markup);
    }
}
