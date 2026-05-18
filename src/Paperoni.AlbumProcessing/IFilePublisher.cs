namespace Paperoni.AlbumProcessing;

public enum PublisherTarget
{
    Markdown,
    Pdf
}

public interface IFilePublisher
{
    Task PublishFileAsync(int albumId, CancellationToken stoppingToken);
    Task DeletePreviousAsync(string title, CancellationToken stoppingToken);
}
