namespace Paperoni.Ai;

public interface IPipelineService
{
    Task<PipelineRunResult> RunAsync(
        int albumId,
        Action<DebugOutputType, string>? statusCallback = null,
        CancellationToken stoppingToken = default);
}
