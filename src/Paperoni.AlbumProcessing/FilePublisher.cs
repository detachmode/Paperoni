using Paperoni.Ai;
using Paperoni.Contract;

namespace Paperoni.AlbumProcessing;

internal sealed class FilePublisher(
    AlbumWorkingDirectory workingDirectory,
    string outputPath,
    string searchPattern) : IFilePublisher
{
    public async Task PublishFileAsync(int msgId, CancellationToken stoppingToken)
    {
        var aiResult = await workingDirectory.RequireData<AiResult>(msgId, stoppingToken);
        var workingDir = workingDirectory.GetDownloadPath(msgId);
        var file = Directory.GetFiles(workingDir, searchPattern, SearchOption.TopDirectoryOnly).FirstOrDefault();
        ArgumentNullException.ThrowIfNull(file);

        var destPath = Path.Combine(outputPath, $"{aiResult.Title}{Path.GetExtension(file)}");
        Directory.CreateDirectory(outputPath);
        File.Copy(file, destPath, overwrite: true);
    }
}
