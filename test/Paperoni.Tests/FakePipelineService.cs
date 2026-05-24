using Paperoni.Ai;

namespace Paperoni.Tests;

internal sealed class FakePipelineService : IPipelineService
{
    public bool ShouldThrow { get; set; }

    public async Task<PipelineRunResult> RunAsync(
        PipelineScript script,
        int albumId,
        Action<DebugOutputType, string>? statusCallback = null,
        CancellationToken stoppingToken = default)
    {
        if (ShouldThrow)
        {
            throw new TimeoutException("AI summary timed out.");
        }

        statusCallback?.Invoke(DebugOutputType.Reasoning, "AI thinking..");
        await Task.Delay(10, stoppingToken);
        statusCallback?.Invoke(DebugOutputType.PartialOutput, "AI is formulating the final output ..");
        await Task.Delay(10, stoppingToken);

        var filename = "Lorem Ipsum";
        var formatted = $"""
            ---
            title: {filename}
            ---
            # Summary
            Fake AI summary for testing.
            """;

        return new PipelineRunResult(filename, formatted);
    }
}

internal sealed class FakeScriptLoader : IScriptLoader
{
    public Task<PipelineScript> LoadAsync(string scriptPath, ScriptGlobals? globals = null)
    {
        var schema = typeof(FakeTestNote);
        var prompt = "Fake prompt for testing.";
        Func<FakeTestNote, string> getFilename = note => note.Title.Replace(":", " -");
        Func<FakeTestNote, string> format = note => $"---\ntitle: {note.Title}\n---\n{note.MarkdownBody}";

        return Task.FromResult(new PipelineScript
        {
            Schema = schema,
            Prompt = prompt,
            GetFilenameDelegate = getFilename,
            FormatDelegate = format,
            ScriptGlobals = globals ?? new ScriptGlobals([], DateTime.Now)
        });
    }
}

public record FakeTestNote(string Title, string MarkdownBody);