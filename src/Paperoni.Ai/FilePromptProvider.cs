using Paperoni.Contract;

namespace Paperoni.Ai;

public class FilePromptProvider : IPromptProvider
{
    private readonly string _promptFilePath;
    private readonly WorkingDirectory _workingDirectory;

    public FilePromptProvider(AiSettings aiSettings, WorkingDirectory workingDirectory)
    {
        _workingDirectory = workingDirectory;
        ArgumentNullException.ThrowIfNull(aiSettings.PromptFilePath);

        _promptFilePath = aiSettings.PromptFilePath;
        if (!File.Exists(_promptFilePath))
        {
            throw new FileNotFoundException("Prompt file not found", _promptFilePath);
        }
    }

    public async Task<string> GetPromptAsync(int albumId, CancellationToken ct = default)
    {
        var prompt = await File.ReadAllTextAsync(_promptFilePath, ct);

        var metaData = await _workingDirectory.RequireData<MetaData>(albumId, ct);

        var captions = metaData.Caption;
        if (captions.Count > 0)
        {
            prompt += $"\n\nUser's instructions: {string.Join(" | ", captions)}";
        }

        prompt += $"\nCurrent date and time: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        return prompt;
    }
}
