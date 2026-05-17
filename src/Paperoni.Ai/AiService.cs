using System.Diagnostics;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using Paperoni.Telegram;
using static Paperoni.Diagnostics.Diagnostics;

namespace Paperoni.Ai;

internal sealed class AiService : IAiService
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<AiService> _logger;
    private readonly IPromptProvider _promptProvider;
    private readonly ITelegramReplier _telegram;
    private readonly int _timeoutSeconds;
    private readonly AlbumWorkingDirectory _workingDirectory;

    public AiService(AlbumWorkingDirectory workingDirectory, IPromptProvider promptProvider,
        ITelegramReplier telegram, ILogger<AiService> logger, IConfiguration configuration,
        IChatClient chatClient)
    {
        _workingDirectory = workingDirectory;
        _promptProvider = promptProvider;
        _telegram = telegram;
        _logger = logger;
        _chatClient = chatClient;
        _timeoutSeconds = int.TryParse(configuration["AiTimeoutSeconds"], out var t) ? t : 600;
    }

    public async Task<string> TryFunctionCalling()
    {
        var weatherFunction = AIFunctionFactory.Create(
            (string location, string unit) => { return "Periods of rain or drizzle, 15 C"; },
            "get_current_weather",
            "Gets the current weather in a given location");

        List<ChatMessage> chatHistory =
        [
            new(ChatRole.System, """
                                 You are a hiking enthusiast who helps people discover fun hikes in their area. You are upbeat and friendly.
                                 """),
            new(ChatRole.User,
                "I live in Montreal and I'm looking for a moderate intensity hike. What's the current weather like?")
        ];

        Console.WriteLine($"{chatHistory.Last().Role} >>> {chatHistory.Last()}");

        var response = await _chatClient.GetResponseAsync(chatHistory, new ChatOptions { Tools = [weatherFunction] });

        Console.WriteLine($"Assistant >>> {response.Text}");
        return response.Text;
    }

    public async Task<string> AskWithFilesAsync(IEnumerable<FileContent> files, string prompt,
        Action<DebugOutputType, string>? debugOutput = null,
        CancellationToken cancellationToken = default)
    {
        var message = new ChatMessage(ChatRole.User, prompt);
        foreach (var file in files)
        {
            message.Contents.Add(new DataContent(file.Data, file.MediaType));
        }

        var sawFirstChunk = false;
        var st = Stopwatch.StartNew();
        var fullResponse = "";
        var reasoningLine = "";
        var partialUpdateLine = "";
        var chunkCount = 0;

        await foreach (var update in _chatClient.GetStreamingResponseAsync(message,
                           cancellationToken: cancellationToken))
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

        debugOutput?.Invoke(DebugOutputType.PartialOutput, partialUpdateLine.Replace(Environment.NewLine, ""));
        _logger.AiStreamingDone(chunkCount, st.Elapsed.TotalSeconds);
        return fullResponse;
    }

    public async Task CreateAiSummary(int msgId,
        CancellationToken stoppingToken = default)
    {
        using var activity = Tracer.StartActivity<AiService>();
        activity?.SetTag("AlbumId", msgId);

        var workingDir = _workingDirectory.RequireWorkingDirectory(msgId);
        var files =
            Directory
                .GetFiles(workingDir)
                .Where(FileHelpers.IsImageFile).ToList();
        activity?.SetTag("fileCount", files.Count);
        var fileContents = files
            .Select(f => new FileContent(
                File.ReadAllBytes(f),
                GetMediaType(f)
            )).ToList();

        var prompt = await _promptProvider.GetPromptAsync(msgId, stoppingToken);
        DebugOutputType? lastDebugType = null;
        var sw = Stopwatch.StartNew();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));
        string aiResult;
        try
        {
            aiResult = await AskWithFilesAsync(fileContents, prompt, (t, s) =>
            {
                if (lastDebugType == t)
                {
                    return;
                }

                lastDebugType = t;
                _ = t switch
                {
                    DebugOutputType.Reasoning => _telegram.EditReply(msgId, "🤖 AI is thinking .."),
                    DebugOutputType.PartialOutput => _telegram.EditReply(msgId,
                        "🤖 AI is formulating the final output .."),
                    _ => Task.CompletedTask
                };
            }, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            throw new TimeoutException("AI summary timed out after 10 minutes.");
        }

        sw.Stop();

        await File.WriteAllTextAsync(Path.Combine(workingDir, "firstAiResponse.md"), aiResult, stoppingToken);

        aiResult = MarkdownHelper.FixMarkdownFromAi(aiResult);
        var title = MarkdownHelper.GetTitleFromMarkdown(aiResult);

        await _workingDirectory.WriteData(msgId, new AiResult(title), stoppingToken);

        activity?.SetTag("title", title);
        activity?.SetTag("length", aiResult.Length);
        activity?.SetTag("latencySec", sw.Elapsed.TotalSeconds);
        activity?.SetStatus(ActivityStatusCode.Ok);

        _logger.AiSummaryCompleted(aiResult.Length, sw.Elapsed.TotalSeconds, title);
    }

    public async Task<string> Review(IEnumerable<FileContent> files, string firstPrompt, string answer,
        CancellationToken cancellationToken = default)
    {
        var systemPrompt = new ChatMessage(ChatRole.System,
            "You are reviewing the output of another AI response. Please double check that it followed the prompt and fix mistakes. Focus on formatting, tagging and correctness. Return the final polished output only.");

        var userPrompt = new ChatMessage(ChatRole.User,
            $"-------- the inital prompt ---------\n{firstPrompt}\n-------- end of inital prompt ---------\n" +
            $"-------- the answer of the AI ---------\n{answer}\n-------- end of answer of the AI ---------");

        foreach (var file in files)
        {
            userPrompt.Contents.Add(new DataContent(file.Data, file.MediaType));
        }

        var response =
            await _chatClient.GetResponseAsync([systemPrompt, userPrompt], cancellationToken: cancellationToken);
        return response.Text;
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

public enum DebugOutputType
{
    Timing,
    Reasoning,
    PartialOutput
}
