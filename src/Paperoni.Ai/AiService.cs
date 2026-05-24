using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Paperoni.Ai;

internal sealed class AiService : IAiService
{
    private readonly IChatClient _chatClient;
    private readonly ILogger<AiService> _logger;
    private readonly int _timeoutSeconds;

    public AiService(ILogger<AiService> logger, AiSettings aiSettings, IChatClient chatClient)
    {
        _logger = logger;
        _chatClient = chatClient;
        _timeoutSeconds = aiSettings.TimeoutSeconds;
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

        var response = await _chatClient.GetResponseAsync(chatHistory, new ChatOptions { Tools = [weatherFunction] });
        return response.Text;
    }

    public Task<string> AskWithFilesAsync(IEnumerable<FileContent> files, string prompt,
        Action<DebugOutputType, string>? debugOutput = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Use IPipelineService.RunAsync instead.");
    }

    public Task CreateAiSummary(int albumId, Action<DebugOutputType, string>? statusCallback = null,
        CancellationToken stoppingToken = default)
    {
        throw new NotSupportedException("Use IPipelineService.RunAsync instead.");
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
}

public enum DebugOutputType
{
    Timing,
    Reasoning,
    PartialOutput
}