using Microsoft.Extensions.Configuration;
using Paperoni.Contract;

namespace Paperoni.Ai;

public class FilePromptProvider : IPromptProvider
{
    private readonly AlbumWorkingDirectory _workingDirectory;
    private readonly string _promptFilePath;

    public FilePromptProvider(IConfiguration configuration, AlbumWorkingDirectory workingDirectory)
    {
        _workingDirectory = workingDirectory;
        var fromConfig = configuration["PromptFilePath"];
        ArgumentNullException.ThrowIfNull(fromConfig);

        _promptFilePath = fromConfig;
        if (!File.Exists(_promptFilePath))
            throw new FileNotFoundException("Prompt file not found", _promptFilePath);
    }

    public async Task<string> GetPromptAsync(int msgId, CancellationToken ct = default)
    {
        var prompt = await File.ReadAllTextAsync(_promptFilePath, ct);

        var metaData = await _workingDirectory.RequireData<MetaData>(msgId, ct);
        
        var captions = metaData.Caption;
        if (captions.Count > 0)
        {
            prompt += $"\n\nUser's instructions: {string.Join(" | ", captions)}";
        }

        prompt += $"\nCurrent date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        return prompt;
    }
}