using System.Diagnostics;
using Paperoni.Ai;
using Paperoni.Contract;
using static Paperoni.Diagnostics.Diagnostics;

namespace Paperoni.Tests;

internal sealed class FakeAiService(AlbumWorkingDirectory workingDirectory) : IAiService
{
    public bool ShouldThrowOnCreateAiSummary { get; set; }

    public Task<string> AskWithFilesAsync(IEnumerable<FileContent> files, string prompt,
        Action<DebugOutputType, string>? debugOutput = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("Fake AI response for: " + prompt);
    }

    public Task<string> TryFunctionCalling() => Task.FromResult("Fake function calling response");

    public async Task CreateAiSummary(int msgId, CancellationToken stoppingToken = default)
    {
        if (ShouldThrowOnCreateAiSummary)
        {
            throw new TimeoutException("AI summary timed out.");
        }

        using var activity = Tracer.StartActivity("AiService.CreateAiSummary");
        activity?.SetTag("AlbumId", msgId);

        var workDir = workingDirectory.RequireWorkingDirectory(msgId);

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

        var aiResult = new AiResult("Lorem Ipsum");
        var json = System.Text.Json.JsonSerializer.Serialize(aiResult,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(workDir, "AiResult.json"), json, stoppingToken);

        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}
