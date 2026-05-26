using Paperoni.Contract;

namespace Paperoni;

public sealed class WorkingDirectoryCleanup(
    WorkingDirectory workingDirectory,
    ILogger<WorkingDirectoryCleanup> logger,
    AlbumProcessingSettings settings) : BackgroundService
{
    private static readonly TimeSpan s_interval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunCleanup();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(s_interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RunCleanup();
        }
    }

    public Task RunCleanup()
    {
        var retentionDays = settings.WorkingDirectoryRetentionDays;
        if (retentionDays <= 0)
        {
            return Task.CompletedTask;
        }

        var cutoff = DateTime.UtcNow.AddDays(-retentionDays);
        var basePath = workingDirectory.BasePath;

        if (!Directory.Exists(basePath))
        {
            return Task.CompletedTask;
        }

        var deleted = 0;
        foreach (var dir in Directory.GetDirectories(basePath))
        {
            var dirName = Path.GetFileName(dir);
            if (!int.TryParse(dirName, out var albumId))
            {
                continue;
            }

            var lastWrite = Directory.GetLastWriteTimeUtc(dir);
            if (lastWrite >= cutoff)
            {
                continue;
            }

            try
            {
                workingDirectory.DeleteAlbumDirectory(albumId);
                logger.WorkingDirectoryDeleted(albumId);
                deleted++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete working directory for album {AlbumId}", albumId);
            }
        }

        if (deleted > 0)
        {
            logger.WorkingDirectoryCleanupComplete(deleted, retentionDays);
        }

        return Task.CompletedTask;
    }
}
