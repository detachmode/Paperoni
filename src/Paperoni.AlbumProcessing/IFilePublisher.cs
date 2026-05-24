namespace Paperoni.AlbumProcessing;

public enum PublisherTarget
{
    Markdown,
    Pdf
}

public interface IFilePublisher
{
    Task PublishStringAsync(string content, string filename, CancellationToken stoppingToken);
    Task PublishFileAsync(string sourcePath, string filename, CancellationToken stoppingToken);
    Task DeletePreviousAsync(string filename, string extension, CancellationToken stoppingToken);
}