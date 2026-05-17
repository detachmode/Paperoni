using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using static Paperoni.Diagnostics.Diagnostics;

namespace Paperoni.AlbumProcessing;

internal sealed class FilePublisher(
    AlbumWorkingDirectory workingDirectory,
    string outputPath,
    string searchPattern,
    AlbumIdAccessor albumIdAccessor,
    ILogger<FilePublisher> logger) : IFilePublisher
{
    private readonly string _extension = Path.GetExtension(searchPattern);

    public async Task PublishFileAsync(int msgId, CancellationToken stoppingToken)
    {
        using var activity = Tracer.StartActivity<FilePublisher>();
        activity?.SetTag("AlbumId", msgId);
        activity?.SetTag("type", _extension.TrimStart('.').ToUpperInvariant());

        var aiResult = await workingDirectory.RequireData<AiResult>(msgId, stoppingToken);
        var workingDir = workingDirectory.RequireWorkingDirectory(msgId);
        var file = Directory.GetFiles(workingDir, searchPattern, SearchOption.TopDirectoryOnly).FirstOrDefault();
        ArgumentNullException.ThrowIfNull(file);

        var destPath = Path.Combine(outputPath, $"{aiResult.Title}{_extension}");
        Directory.CreateDirectory(outputPath);

        var fileInfo = new FileInfo(file);
        File.Copy(file, destPath, overwrite: true);

        activity?.SetTag("source", Path.GetFileName(file));
        activity?.SetTag("dest", destPath);
        activity?.SetTag("sizeKb", fileInfo.Length / 1024);
        activity?.SetStatus(ActivityStatusCode.Ok);

        logger.FilePublished(_extension.TrimStart('.').ToUpperInvariant(),
            Path.GetFileName(file), destPath, fileInfo.Length / 1024);
    }

    public Task DeletePreviousAsync(string title, CancellationToken stoppingToken)
    {
        using var activity = Tracer.StartActivity<FilePublisher>();
        activity?.SetTag("AlbumId", albumIdAccessor.Id);
        activity?.SetTag("type", _extension.TrimStart('.').ToUpperInvariant());

        var filePath = Path.Combine(outputPath, $"{title}{_extension}");
        activity?.SetTag("fileToDelete", Path.GetFileName(filePath));

        if (File.Exists(filePath))
        {
            File.Delete(filePath);
            logger.FileDeleted(_extension.TrimStart('.').ToUpperInvariant(), filePath);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        return Task.CompletedTask;
    }
}
