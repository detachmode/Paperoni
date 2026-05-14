using System.ClientModel;
using System.Diagnostics;
using Microsoft.Extensions.AI;
using OpenAI;
using Paperoni.Contract;
using Paperoni.Telegram;

namespace Paperoni.Ai;

internal class AiService : IAiService, IDisposable
{
    private readonly AlbumWorkingDirectory _workingDirectory;
    private readonly IPromptProvider _promptProvider;
    private readonly ITelegramReplier _telegram;
    private readonly IChatClient _chatClient;

    public AiService(AlbumWorkingDirectory workingDirectory, IPromptProvider promptProvider, ITelegramReplier telegram)
    {
        _workingDirectory = workingDirectory;
        _promptProvider = promptProvider;
        _telegram = telegram;

        var endpoint = Environment.GetEnvironmentVariable("AI_ENDPOINT") ?? "http://localhost:2276";
        var model = Environment.GetEnvironmentVariable("AI_MODEL") ?? "qwen-3.6-35b-a3b-q4";
        var client = new OpenAIClient(new ApiKeyCredential("api-key"),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        _chatClient = client.GetChatClient(model).AsIChatClient();
    }


    public async Task<string> AskWithFilesAsync(IEnumerable<FileContent> files, string prompt,
        Action<DebugOutputType, string> debugOutput = null,
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
        await foreach (var update in _chatClient.GetStreamingResponseAsync(message,
                           cancellationToken: cancellationToken))
        {
            fullResponse += update.Text;

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
                debugOutput?.Invoke(DebugOutputType.Timing,
                    $"[Debug]time to first chunk: {st.ElapsedMilliseconds} ms" + Environment.NewLine);
            }
        }

        debugOutput?.Invoke(DebugOutputType.PartialOutput, partialUpdateLine.Replace(Environment.NewLine, ""));
        return fullResponse;
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

    public async Task CreateAiSummary(int msgId,
        CancellationToken stoppingToken = default)
    {
        var workingDir = _workingDirectory.GetDownloadPath(msgId);
        var files =
            Directory
                .GetFiles(workingDir)
                .Where(FileHelpers.IsImageFile).ToList();
        var fileContents = files
            .Select(f => new FileContent(
                File.ReadAllBytes(f),
                GetMediaType(f)
            )).ToList();

        var prompt = await _promptProvider.GetPromptAsync(msgId, stoppingToken);
        DebugOutputType? lastDebugType = null;
        var aiResult = await AskWithFilesAsync(fileContents, prompt, (t, s) =>
        {
            if (lastDebugType == t) return;
            _ = t switch
            {
                DebugOutputType.Reasoning => _telegram.EditReply(msgId, "🤖 AI is thinking .."),
                DebugOutputType.PartialOutput => _telegram.EditReply(msgId, "🤖 AI is formulating the final output .."),
                _ => Task.CompletedTask
            };
        }, stoppingToken);
        await File.WriteAllTextAsync(Path.Combine(workingDir, "firstAiResponse.md"), aiResult, stoppingToken);

        aiResult = MarkdownHelper.FixMarkdownFromAi(aiResult);
        var title = MarkdownHelper.GetTitleFromMarkdown(aiResult);

        await _workingDirectory.WriteData(msgId, new AiResult(title), stoppingToken);
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

    public void Dispose()
    {
        (_chatClient as IDisposable)?.Dispose();
    }
}

public enum DebugOutputType
{
    Timing,
    Reasoning,
    PartialOutput
}