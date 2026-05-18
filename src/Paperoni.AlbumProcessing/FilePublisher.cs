using Microsoft.Extensions.Logging;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using static Paperoni.Diagnostics.Diagnostics;

namespace Paperoni.AlbumProcessing;

internal sealed class FilePublisher(
    AlbumWorkingDirectory workingDirectory,
    string outputPath,
    string searchPattern,
    ILogger<FilePublisher> logger) : IFilePublisher
{
    private readonly string _extension = Path.GetExtension(searchPattern);

    public async Task PublishFileAsync(int albumId, CancellationToken stoppingToken)
    {
        await Tracer.TraceAsync<FilePublisher>(async scope =>
        {
            scope.SetTag("type", _extension.TrimStart('.').ToUpperInvariant());

            var aiResult = await workingDirectory.RequireData<AiResult>(albumId, stoppingToken);
            var workingDir = workingDirectory.RequireWorkingDirectory(albumId);
            var file = Directory.GetFiles(workingDir, searchPattern, SearchOption.TopDirectoryOnly).FirstOrDefault();
            ArgumentNullException.ThrowIfNull(file);

            var destPath = Path.Combine(outputPath, $"{aiResult.Title}{_extension}");
            Directory.CreateDirectory(outputPath);

            var fileInfo = new FileInfo(file);
            File.Copy(file, destPath, overwrite: true);

            scope.SetTag("source", Path.GetFileName(file));
            scope.SetTag("dest", destPath);
            scope.SetTag("sizeKb", fileInfo.Length / 1024);

            logger.FilePublished(_extension.TrimStart('.').ToUpperInvariant(),
                Path.GetFileName(file), destPath, fileInfo.Length / 1024);
        });
    }

    public async Task DeletePreviousAsync(string title, CancellationToken stoppingToken)
    {
        await Tracer.TraceAsync<FilePublisher>(async scope =>
        {
            scope.SetTag("type", _extension.TrimStart('.').ToUpperInvariant());

            var filePath = Path.Combine(outputPath, $"{title}{_extension}");
            scope.SetTag("fileToDelete", Path.GetFileName(filePath));

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                logger.FileDeleted(_extension.TrimStart('.').ToUpperInvariant(), filePath);
            }
        });
    }
}
