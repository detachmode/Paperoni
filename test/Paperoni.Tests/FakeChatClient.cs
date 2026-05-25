using Microsoft.Extensions.AI;

namespace Paperoni.Tests;

internal sealed class FakeChatClient : IChatClient
{
    public bool ShouldThrow { get; set; }
    public List<string> Responses { get; set; } = [];
    public int InvocationCount { get; private set; }
    public ChatClientMetadata Metadata { get; } = new("FakeChatClient", new Uri("http://localhost"), "fake-model");

    private string GetResponseJson()
    {
        if (Responses.Count > 0)
        {
            var index = Math.Min(InvocationCount, Responses.Count - 1);
            return Responses[index];
        }

        return """{"Title":"Lorem Ipsum","Summary":"Fake summary for testing","MarkdownBody":"Fake AI summary for testing."}""";
    }

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (ShouldThrow)
        {
            throw new TimeoutException("AI summary timed out.");
        }

        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, GetResponseJson())));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> chatMessages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (ShouldThrow)
        {
            throw new TimeoutException("AI summary timed out.");
        }

        var responseJson = GetResponseJson();
        InvocationCount++;

        yield return new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>
        {
            new TextReasoningContent("some fake thoughts")
        });

        yield return new ChatResponseUpdate(ChatRole.Assistant, responseJson);
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceId = null) => null;
    public void Dispose() { }
}
