namespace Paperoni.Telegram;

public sealed record TelegramPhotoFile(
    long ChatId,
    int MessageId,
    string FileId,
    string FileUniqueId,
    string? Caption,
    DateTime Date
);
