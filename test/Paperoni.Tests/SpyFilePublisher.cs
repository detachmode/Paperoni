using Paperoni.AlbumProcessing;

namespace Paperoni.Tests;

internal sealed class SpyFilePublisher : IFilePublisher
{
    private readonly IFilePublisher _inner;

    public bool DeletePreviousCalled { get; private set; }
    public string? LastDeletedTitle { get; private set; }

    public SpyFilePublisher(IFilePublisher inner)
    {
        _inner = inner;
    }

    public Task PublishFileAsync(int albumId, CancellationToken stoppingToken)
        => _inner.PublishFileAsync(albumId, stoppingToken);

    public Task DeletePreviousAsync(string title, CancellationToken stoppingToken)
    {
        DeletePreviousCalled = true;
        LastDeletedTitle = title;
        return _inner.DeletePreviousAsync(title, stoppingToken);
    }
}
