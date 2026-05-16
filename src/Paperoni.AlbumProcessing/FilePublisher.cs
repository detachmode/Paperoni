using Paperoni.Ai;
using Paperoni.Contract;

namespace Paperoni.AlbumProcessing;

internal sealed class FilePublisher(
    AlbumWorkingDirectory workingDirectory,
    string outputPath,
    string searchPattern) : IFilePublisher
{
    private readonly string _extension = Path.GetExtension(searchPattern);

    public async Task PublishFileAsync(int msgId, CancellationToken stoppingToken)
    {
        var aiResult = await workingDirectory.RequireData<AiResult>(msgId, stoppingToken);
        var workingDir = workingDirectory.GetDownloadPath(msgId);
        var file = Directory.GetFiles(workingDir, searchPattern, SearchOption.TopDirectoryOnly).FirstOrDefault();
        ArgumentNullException.ThrowIfNull(file);

        var destPath = Path.Combine(outputPath, $"{aiResult.Title}{_extension}");
        Directory.CreateDirectory(outputPath);
        File.Copy(file, destPath, overwrite: true);
    }

    public Task DeletePreviousAsync(string title, CancellationToken stoppingToken)
    {
        var filePath = Path.Combine(outputPath, $"{title}{_extension}");
        if (File.Exists(filePath))
            File.Delete(filePath);
        return Task.CompletedTask;
    }
}
