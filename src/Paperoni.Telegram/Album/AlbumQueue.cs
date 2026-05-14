using System.Threading.Channels;
using Paperoni.Contract;

namespace Paperoni.Telegram.Album;

public sealed record AlbumQueueEntry(SortedDictionary<int, TelegramPhotoFile>  Photos);
public class AlbumQueue
{
    private readonly Channel<AlbumQueueEntry> _channel;

    public AlbumQueue()
    {
        _channel = Channel.CreateUnbounded<AlbumQueueEntry>();
    }

    public void Enqueue(AlbumQueueEntry photos)
    {
        _channel.Writer.TryWrite(photos);
    }

    public async Task<AlbumQueueEntry> Dequeue(CancellationToken ct = default)
    {
        return await _channel.Reader.ReadAsync(ct);
    }
}
