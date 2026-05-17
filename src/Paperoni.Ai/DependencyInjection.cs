using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;
using System.ClientModel;

namespace Paperoni.Ai;

public static class DependencyInjection
{
    public static IServiceCollection AddAiService(this IServiceCollection collection)
    {
        var endpoint = Environment.GetEnvironmentVariable("AI_ENDPOINT") ?? "http://localhost:2276";
        var model = Environment.GetEnvironmentVariable("AI_MODEL") ?? "qwen-3.6-35b-a3b-q4";

        collection.AddChatClient(sp =>
            {
                var openAiClient = new OpenAIClient(new ApiKeyCredential("api-key"),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
                return openAiClient.GetChatClient(model).AsIChatClient();
            })
            .UseFunctionInvocation();

        collection.AddSingleton<IPromptProvider, FilePromptProvider>();
        collection.AddSingleton<IAiService, AiService>();
        return collection;
    }
}
