namespace Paperoni.Telegram;

internal sealed class Album
{
    public object SyncRoot { get; } = new();
    public SortedDictionary<int, TelegramPhotoFile> Photos { get; } = new();
    public Timer? Timer { get; set; }
}
