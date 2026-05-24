using Paperoni.AlbumProcessing;

namespace Paperoni.Tests;

internal sealed class SpyFilePublisher : IFilePublisher
{
    private readonly IFilePublisher _inner;

    public bool DeletePreviousCalled { get; private set; }
    public string? LastDeletedFilename { get; private set; }
    public string? LastDeletedExtension { get; private set; }

    public SpyFilePublisher(IFilePublisher inner)
    {
        _inner = inner;
    }

    public Task PublishStringAsync(string content, string filename, CancellationToken stoppingToken)
        => _inner.PublishStringAsync(content, filename, stoppingToken);

    public Task PublishFileAsync(string sourcePath, string filename, CancellationToken stoppingToken)
        => _inner.PublishFileAsync(sourcePath, filename, stoppingToken);

    public Task DeletePreviousAsync(string filename, string extension, CancellationToken stoppingToken)
    {
        DeletePreviousCalled = true;
        LastDeletedFilename = filename;
        LastDeletedExtension = extension;
        return _inner.DeletePreviousAsync(filename, extension, stoppingToken);
    }
}