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
    private readonly int _maxRetries;

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
        _maxRetries = aiSettings.MaxRetries;
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

            var chatOptions = new ChatOptions
            {
                ResponseFormat = ChatResponseFormat.ForJsonSchema(
                    script.Schema,
                    schemaName: script.Schema.Name,
                    schemaDescription: "Follow the script-defined property rules explicitly."
                )
            };

            var conversation = BuildInitialConversation(script.Prompt, fileContents);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Exception? lastError = null;
            string? aiResponse = null;

            for (var attempt = 0; attempt <= _maxRetries; attempt++)
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

                DebugOutputType? lastDebugType = null;
                var isRetry = attempt > 0;

                try
                {
                    aiResponse = await AskWithFilesAsync(conversation, chatOptions,
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
                                    statusCallback?.Invoke(DebugOutputType.Reasoning,
                                        isRetry ? $"🤖 AI retry {attempt}/{_maxRetries} — thinking .." : "🤖 AI is thinking ..");
                                    break;
                                case DebugOutputType.PartialOutput:
                                    statusCallback?.Invoke(DebugOutputType.PartialOutput,
                                        isRetry ? $"🤖 AI retry {attempt}/{_maxRetries} — formulating .." : "🤖 AI is formulating the final output ..");
                                    break;
                            }
                        }, timeoutCts.Token);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    throw new TimeoutException($"AI summary timed out after {_timeoutSeconds} seconds.");
                }

                var responseFileName = attempt == 0 ? "aiResponse.json" : $"aiResponse_attempt{attempt + 1}.json";
                await File.WriteAllTextAsync(
                    Path.Combine(workingDir, responseFileName), aiResponse, stoppingToken);

                try
                {
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

                    sw.Stop();
                    scope.SetTag("title", filename);
                    scope.SetTag("length", aiResponse.Length);
                    scope.SetTag("latencySec", sw.Elapsed.TotalSeconds);
                    scope.SetTag("attempts", attempt + 1);

                    _logger.AiSummaryCompleted(aiResponse.Length, sw.Elapsed.TotalSeconds, filename);

                    return new PipelineRunResult(filename, formatted);
                }
                catch (Exception ex) when (
                    ex is JsonException or InvalidPipelineScriptException or InvalidOperationException)
                {
                    lastError = ex;
                    _logger.AiRetryDeserialization(attempt + 1, _maxRetries + 1, ex.Message);

                    if (attempt < _maxRetries)
                    {
                        _logger.AiRetryAttempt(attempt + 1, _maxRetries);
                        statusCallback?.Invoke(DebugOutputType.PartialOutput,
                            $"🤖 AI retry {attempt + 1}/{_maxRetries} — fixing response ..");

                        conversation.Add(new ChatMessage(ChatRole.Assistant, aiResponse));
                        conversation.Add(new ChatMessage(ChatRole.User,
                            BuildRetryPrompt(ex.Message, aiResponse)));
                    }
                }
            }

            throw lastError!;
        });
    }

    private static List<ChatMessage> BuildInitialConversation(string prompt, IEnumerable<FileContent> files)
    {
        var message = new ChatMessage(ChatRole.User, prompt);
        foreach (var file in files)
        {
            message.Contents.Add(new DataContent(file.Data, file.MediaType));
        }

        return [message];
    }

    private static string BuildRetryPrompt(string error, string badResponse)
    {
        return $"""
            Your previous response could not be deserialized. Fix the JSON to match the schema.

            Error: {error}

            Your previous response:
            {badResponse}

            Respond with corrected JSON only. Do not include any explanation.
            """;
    }

    private async Task<string> AskWithFilesAsync(
        List<ChatMessage> conversation,
        ChatOptions chatOptions,
        Action<DebugOutputType, string>? debugOutput = null,
        CancellationToken cancellationToken = default)
    {
        var sawFirstChunk = false;
        var st = System.Diagnostics.Stopwatch.StartNew();
        var fullResponse = "";
        var sentPartialOutput = false;
        var chunkCount = 0;

        await foreach (var update in _chatClient.GetStreamingResponseAsync(conversation,
                           chatOptions, cancellationToken))
        {
            fullResponse += update.Text;
            if (!string.IsNullOrEmpty(update.Text))
            {
                chunkCount++;
            }

            if (!sentPartialOutput && !string.IsNullOrWhiteSpace(update.Text))
            {
                debugOutput?.Invoke(DebugOutputType.PartialOutput, update.Text);
                sentPartialOutput = true;
            }

            var reasoning = update.Contents.FirstOrDefault() as TextReasoningContent;
            if (!string.IsNullOrWhiteSpace(reasoning?.Text))
            {
                debugOutput?.Invoke(DebugOutputType.Reasoning, reasoning.Text);
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
