using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using static Paperoni.Diagnostics.Diagnostics;

namespace Paperoni.Telegram.Album;

public sealed record WorkItem(int MessageId, bool IsRetry);

public class AlbumQueue
{
    private readonly Channel<WorkItem> _channel;
    private readonly ILogger<AlbumQueue> _logger;
    private int _pendingCount;

    public int PendingCount => _pendingCount;

    public AlbumQueue(ILogger<AlbumQueue> logger)
    {
        _channel = Channel.CreateUnbounded<WorkItem>();
        _logger = logger;
    }

    public int Enqueue(WorkItem item)
    {
        var depth = Interlocked.Increment(ref _pendingCount);
        if (!_channel.Writer.TryWrite(item))
        {
            Interlocked.Decrement(ref _pendingCount);
            throw new InvalidOperationException($"Failed to enqueue album {item.MessageId}");
        }
        _logger.AlbumEnqueued(item.MessageId, item.IsRetry, depth);
        return depth;
    }

    public async Task<WorkItem> Dequeue(CancellationToken ct = default)
    {
        var item = await _channel.Reader.ReadAsync(ct);
        var depth = Interlocked.Decrement(ref _pendingCount);
        _logger.AlbumDequeued(item.MessageId, depth);
        return item;
    }

}
