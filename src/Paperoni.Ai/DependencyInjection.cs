using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenAI;

namespace Paperoni.Ai;

public static class DependencyInjection
{
    public static IServiceCollection AddAiService(this IServiceCollection collection)
    {
        collection.AddChatClient(sp =>
            {
                var config = sp.GetRequiredService<IConfiguration>();

                var endpoint = config["Ai:Endpoint"];
                ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

                var model = config["Ai:Model"];
                ArgumentException.ThrowIfNullOrWhiteSpace(model);

                var apiKey = config["AI_API_KEY"] ?? "api-key";
                var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey),
                    new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
                return openAiClient.GetChatClient(model).AsIChatClient();
            })
            .UseFunctionInvocation();

        collection.AddSingleton<IPromptProvider, FilePromptProvider>();
        collection.AddSingleton<IAiService, AiService>();
        return collection;
    }
}
