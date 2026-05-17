namespace Paperoni.Contract;

public sealed record TelegramPhotoFile(
    long ChatId,
    int MessageId,
    string FileId,
    string FileUniqueId,
    string? Caption,
    DateTime Date
);
