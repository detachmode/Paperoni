using Microsoft.Extensions.Logging;
using Paperoni.Diagnostics;
using static Paperoni.Diagnostics.Diagnostics;

namespace Paperoni.AlbumProcessing;

internal sealed class FilePublisher(
    string outputPath,
    ILogger<FilePublisher> logger) : IFilePublisher
{
    public async Task PublishStringAsync(string content, string filename, CancellationToken stoppingToken)
    {
        await Tracer.TraceAsync<FilePublisher>(async scope =>
        {
            scope.SetTag("type", "MD");

            Directory.CreateDirectory(outputPath);
            var destPath = Path.Combine(outputPath, $"{filename}.md");
            await File.WriteAllTextAsync(destPath, content, stoppingToken);

            scope.SetTag("dest", destPath);
            scope.SetTag("sizeKb", content.Length / 1024);

            logger.FilePublished("MD", filename + ".md", destPath, content.Length / 1024);
        });
    }

    public async Task PublishFileAsync(string sourcePath, string filename, CancellationToken stoppingToken)
    {
        await Tracer.TraceAsync<FilePublisher>(async scope =>
        {
            scope.SetTag("type", "PDF");

            if (!File.Exists(sourcePath))
            {
                throw new FileNotFoundException("PDF file not found in working directory", sourcePath);
            }

            Directory.CreateDirectory(outputPath);
            var destPath = Path.Combine(outputPath, $"{filename}.pdf");
            File.Copy(sourcePath, destPath, overwrite: true);

            var fileInfo = new FileInfo(sourcePath);
            scope.SetTag("dest", destPath);
            scope.SetTag("sizeKb", fileInfo.Length / 1024);

            logger.FilePublished("PDF", filename + ".pdf", destPath, fileInfo.Length / 1024);
        });
    }

    public async Task DeletePreviousAsync(string filename, string extension, CancellationToken stoppingToken)
    {
        await Tracer.TraceAsync<FilePublisher>(async scope =>
        {
            scope.SetTag("type", extension.TrimStart('.').ToUpperInvariant());

            var filePath = Path.Combine(outputPath, $"{filename}{extension}");
            scope.SetTag("fileToDelete", Path.GetFileName(filePath));

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                logger.FileDeleted(extension.TrimStart('.').ToUpperInvariant(), filePath);
            }
        });
    }
}