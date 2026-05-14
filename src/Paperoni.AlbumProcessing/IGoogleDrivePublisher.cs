namespace Paperoni.AlbumProcessing;

public interface IGoogleDrivePublisher
{
    Task CopyToGoogleDrive(int msgId, CancellationToken stoppingToken);
}