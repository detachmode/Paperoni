namespace Paperoni.Ai;

public interface IPipelineService
{
    Task<PipelineRunResult> RunAsync(
        PipelineScript script,
        int albumId,
        Action<DebugOutputType, string>? statusCallback = null,
        CancellationToken stoppingToken = default);
}