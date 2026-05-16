namespace Paperoni.AlbumProcessing;

public enum PublisherTarget
{
    Markdown,
    Pdf
}

public interface IFilePublisher
{
    Task PublishFileAsync(int msgId, CancellationToken stoppingToken);
    Task DeletePreviousAsync(string title, CancellationToken stoppingToken);
}
