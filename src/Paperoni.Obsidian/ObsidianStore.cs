using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Paperoni.Contract;

namespace Paperoni.Obsidian;

public interface IObsidianStore
{
    Task WriteFile(int msgId);
}

internal class ObsidianStore(
    ILogger<ObsidianStore> logger,
    AlbumWorkingDirectory workingDirectory,
    IConfiguration configuration) : IObsidianStore
{
    private readonly string _outputPath = configuration["ObsidianOutputPath"] ?? throw new InvalidOperationException("Configuration key 'ObsidianOutputPath' is not set");

    public async Task WriteFile(int msgId)
    {
        var dir = workingDirectory.GetDownloadPath(msgId);
        var aiResult = await workingDirectory.GetData<AiResult>(msgId);
        ArgumentNullException.ThrowIfNull(aiResult);
        var markdownFilePath = Directory.GetFiles(dir, "*.md").Single();

        var finalPath = Path.Combine(_outputPath, aiResult.Title + ".md");
        Directory.CreateDirectory(_outputPath);
        File.Copy(markdownFilePath, finalPath, overwrite: true);
        logger.LogInformation("Writing {Title} to {Path}", aiResult.Title, finalPath);
    }
}