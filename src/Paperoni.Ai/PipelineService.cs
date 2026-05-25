using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using static Paperoni.Diagnostics.Diagnostics;

namespace Paperoni.Ai;

internal sealed class PipelineService : IPipelineService
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<PipelineService> _logger;
    private readonly WorkingDirectory _workingDirectory;
    private readonly int _timeoutSeconds;

    public PipelineService(
        WorkingDirectory workingDirectory,
        ILogger<PipelineService> logger,
        AiSettings aiSettings,
        IChatClient chatClient)
    {
        _workingDirectory = workingDirectory;
        _logger = logger;
        _chatClient = chatClient;
        _timeoutSeconds = aiSettings.TimeoutSeconds;
    }

    public async Task<PipelineRunResult> RunAsync(
        PipelineScript script,
        int albumId,
        Action<DebugOutputType, string>? statusCallback = null,
        CancellationToken stoppingToken = default)
    {
        return await Tracer.TraceAsync<PipelineService, PipelineRunResult>(async scope =>
        {
            var workingDir = _workingDirectory.RequireWorkingDirectory(albumId);
            var files = Directory.GetFiles(workingDir)
                .Where(FileHelpers.IsImageFile)
                .ToList();

            scope.SetTag("fileCount", files.Count);
            var fileContents = files.Select(f => new FileContent(
                File.ReadAllBytes(f),
                GetMediaType(f)
            )).ToList();

            var prompt = script.Prompt;
            var chatOptions = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    script.Schema,
                    schemaName: script.Schema.Name,
                    schemaDescription: "Follow the script-defined property rules explicitly."
                )
            };

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

            string aiResponse;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            DebugOutputType? lastDebugType = null;

            try
            {
                aiResponse = await AskWithFilesAsync(fileContents, prompt, chatOptions,
                    (t, s) =>
                    {
                        if (lastDebugType == t)
                        {
                            return;
                        }

                        lastDebugType = t;
                        switch (t)
                        {
                            case DebugOutputType.Reasoning:
                                statusCallback?.Invoke(DebugOutputType.Reasoning, "AI is thinking ..");
                                break;
                            case DebugOutputType.PartialOutput:
                                statusCallback?.Invoke(DebugOutputType.PartialOutput, "AI is formulating the final output ..");
                                break;
                        }
                    }, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                throw new TimeoutException($"AI summary timed out after {_timeoutSeconds} seconds.");
            }

            sw.Stop();

            await File.WriteAllTextAsync(
                Path.Combine(workingDir, "firstAiResponse.json"), aiResponse, stoppingToken);

            var record = DeserializeToType(aiResponse, script.Schema);
            var filename = script.InvokeGetFilename(record);
            var formatted = script.InvokeFormat(record);

            if (string.IsNullOrWhiteSpace(filename))
            {
                throw new InvalidOperationException($"GetFilename returned empty for album {albumId}");
            }

            var pipelineResult = new PipelineResult(filename,
                JsonSerializer.Deserialize<Dictionary<string, object>>(aiResponse,
                    JsonSerializerOptions.Web) ?? []);

            await _workingDirectory.WriteData(albumId, pipelineResult, stoppingToken);

            scope.SetTag("title", filename);
            scope.SetTag("length", aiResponse.Length);
            scope.SetTag("latencySec", sw.Elapsed.TotalSeconds);

            _logger.AiSummaryCompleted(aiResponse.Length, sw.Elapsed.TotalSeconds, filename);

            return new PipelineRunResult(filename, formatted);
        });
    }

    private async Task<string> AskWithFilesAsync(
        IEnumerable<FileContent> files,
        string prompt,
        ChatOptions chatOptions,
        Action<DebugOutputType, string>? debugOutput = null,
        CancellationToken cancellationToken = default)
    {
        var message = new ChatMessage(ChatRole.User, prompt);
        foreach (var file in files)
        {
            message.Contents.Add(new DataContent(file.Data, file.MediaType));
        }

        var sawFirstChunk = false;
        var st = System.Diagnostics.Stopwatch.StartNew();
        var fullResponse = "";
        var reasoningLine = "";
        var partialUpdateLine = "";
        var chunkCount = 0;

        await foreach (var update in _chatClient.GetStreamingResponseAsync(message,
                           chatOptions, cancellationToken))
        {
            fullResponse += update.Text;
            if (!string.IsNullOrEmpty(update.Text))
            {
                chunkCount++;
            }

            partialUpdateLine += update.Text;
            if (update.Text.Contains(Environment.NewLine))
            {
                debugOutput?.Invoke(DebugOutputType.PartialOutput, partialUpdateLine.Replace(Environment.NewLine, ""));
                partialUpdateLine = "";
            }

            var reasoning = update.Contents.FirstOrDefault() as TextReasoningContent;
            reasoningLine += reasoning?.Text ?? "";
            if (reasoning?.Text == Environment.NewLine)
            {
                debugOutput?.Invoke(DebugOutputType.Reasoning, reasoningLine.Replace(Environment.NewLine, ""));
                reasoningLine = "";
            }

            if (!sawFirstChunk)
            {
                sawFirstChunk = true;
                var ttf = st.ElapsedMilliseconds;
                _logger.TimeToFirstChunk(ttf);
                debugOutput?.Invoke(DebugOutputType.Timing,
                    $"[Debug]time to first chunk: {ttf} ms" + Environment.NewLine);
            }
        }

        _logger.AiStreamingDone(chunkCount, st.Elapsed.TotalSeconds);
        return fullResponse;
    }

    private static object DeserializeToType(string json, Type type)
    {
        return JsonSerializer.Deserialize(json, type, JsonSerializerOptions.Web)
               ?? throw new InvalidOperationException($"Failed to deserialize AI response as {type.Name}");
    }

    private static string GetMediaType(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }
}

public record PipelineRunResult(string Filename, string FormattedContent);
