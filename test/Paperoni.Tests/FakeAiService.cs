using Paperoni.Ai;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using static Paperoni.Diagnostics.Diagnostics;

namespace Paperoni.Tests;

internal sealed class FakeAiService(WorkingDirectory workingDirectory) : IAiService
{
    public bool ShouldThrowOnCreateAiSummary { get; set; }

    public Task<string> AskWithFilesAsync(IEnumerable<FileContent> files, string prompt,
        Action<DebugOutputType, string>? debugOutput = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("Fake AI response for: " + prompt);
    }

    public Task<string> TryFunctionCalling() => Task.FromResult("Fake function calling response");

    public async Task CreateAiSummary(int albumId, Action<DebugOutputType, string>? statusCallback = null,
        CancellationToken stoppingToken = default)
    {
        await Tracer.TraceAsync<AiService>(async scope =>
        {
            if (ShouldThrowOnCreateAiSummary)
            {
                throw new TimeoutException("AI summary timed out.");
            }

            var workDir = workingDirectory.RequireWorkingDirectory(albumId);

            statusCallback?.Invoke(DebugOutputType.Reasoning, "AI thinking..");
            await Task.Delay(10, stoppingToken);

            statusCallback?.Invoke(DebugOutputType.PartialOutput, "AI is formulating the final output ..");
            await Task.Delay(10, stoppingToken);

            var content = """
                          ---
                          title: Lorem Ipsum
                          ---

                          # Summary
                          Fake AI summary for testing.

                          # Complete Text
                          Lorem ipsum dolor sit amet, consectetur adipiscing elit.
                          """;
            await File.WriteAllTextAsync(Path.Combine(workDir, "firstAiResponse.md"), content, stoppingToken);

            var pipelineResult = new PipelineResult("Lorem Ipsum",
                new Dictionary<string, object> { ["Title"] = "Lorem Ipsum" });
            await workingDirectory.WriteData(albumId, pipelineResult, stoppingToken);
        });
    }
}