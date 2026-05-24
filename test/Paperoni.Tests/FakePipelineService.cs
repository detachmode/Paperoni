using Paperoni.Ai;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using static Paperoni.Diagnostics.Diagnostics;

namespace Paperoni.Tests;

internal sealed class FakePipelineService : IPipelineService
{
    private readonly WorkingDirectory _workingDirectory;
    public bool ShouldThrow { get; set; }

    public FakePipelineService(WorkingDirectory workingDirectory)
    {
        _workingDirectory = workingDirectory;
    }

    public async Task<PipelineRunResult> RunAsync(
        PipelineScript script,
        int albumId,
        Action<DebugOutputType, string>? statusCallback = null,
        CancellationToken stoppingToken = default)
    {
        return await Tracer.TraceAsync<FakePipelineService, PipelineRunResult>(async scope =>
        {
            if (ShouldThrow)
            {
                throw new TimeoutException("AI summary timed out.");
            }

            var workDir = _workingDirectory.RequireWorkingDirectory(albumId);

            statusCallback?.Invoke(DebugOutputType.Reasoning, "AI thinking..");
            await Task.Delay(10, stoppingToken);

            statusCallback?.Invoke(DebugOutputType.PartialOutput, "AI is formulating the final output ..");
            await Task.Delay(10, stoppingToken);

            var filename = "Lorem Ipsum";
            var formatted = "---\ntitle: Lorem Ipsum\n---\n\n# Summary\nFake AI summary for testing.\n\n# Complete Text\nLorem ipsum dolor sit amet.";

            await File.WriteAllTextAsync(Path.Combine(workDir, "firstAiResponse.md"), formatted, stoppingToken);

            var pipelineResult = new PipelineResult(filename,
                new Dictionary<string, object> { ["Title"] = "Lorem Ipsum" });
            await _workingDirectory.WriteData(albumId, pipelineResult, stoppingToken);

            scope.SetTag("title", filename);

            return new PipelineRunResult(filename, formatted);
        });
    }
}

internal sealed class FakeScriptLoader : IScriptLoader
{
    public Task<PipelineScript> LoadAsync(string scriptPath, ScriptGlobals? globals = null)
    {
        var schema = typeof(FakeTestNote);
        Func<FakeTestNote, string> getFilename = note =>
        {
            var safe = MarkdownHelper.AutoFixDate(note.Title ?? "Unknown");
            return MarkdownHelper.SanitizeFilename(safe);
        };
        Func<FakeTestNote, string> format = note =>
        {
            var filename = getFilename(note);
            return "---\ntitle: " + filename + "\n---\n\n# " + note.Summary + "\n\n" + note.MarkdownBody;
        };

        return Task.FromResult(new PipelineScript
        {
            Schema = schema,
            Prompt = "Analyse the document.",
            GetFilenameDelegate = getFilename,
            FormatDelegate = format,
            ScriptGlobals = globals ?? new ScriptGlobals([], DateTime.Now)
        });
    }
}

public record FakeTestNote(string Title, string Summary, string MarkdownBody);