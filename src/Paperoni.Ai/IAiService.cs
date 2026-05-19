namespace Paperoni.Ai;

public interface IAiService
{
    Task<string> AskWithFilesAsync(IEnumerable<FileContent> files, string prompt,
        Action<DebugOutputType, string>? debugOutput = null, CancellationToken cancellationToken = default);

    Task CreateAiSummary(int albumId, Action<DebugOutputType, string>? statusCallback = null, CancellationToken stoppingToken = default);
    Task<string> TryFunctionCalling();
}
