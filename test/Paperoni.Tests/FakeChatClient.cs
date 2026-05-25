using Microsoft.Extensions.AI;

namespace Paperoni.Tests;

internal sealed class FakeChatClient : IChatClient
{
    public bool ShouldThrow { get; set; }
    public ChatClientMetadata Metadata { get; } = new("FakeChatClient", new Uri("http://localhost"), "fake-model");

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> chatMessages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (ShouldThrow)
        {
            throw new TimeoutException("AI summary timed out.");
        }

        var json = """{"Title":"Lorem Ipsum","Summary":"Fake summary for testing","MarkdownBody":"Fake AI summary for testing."}""";
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, json)));
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

        yield return new ChatResponseUpdate(ChatRole.Assistant, new List<AIContent>
        {
            new TextReasoningContent("some fake thoughts")
        });

        var json = """{"Title":"Lorem Ipsum","Summary":"Fake summary for testing","MarkdownBody":"Fake AI summary for testing."}""";
        yield return new ChatResponseUpdate(ChatRole.Assistant, json);
        await Task.CompletedTask;
    }

    public object? GetService(Type serviceType, object? serviceId = null) => null;
    public void Dispose() { }
}
