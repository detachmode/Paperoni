using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Paperoni.Ai;

public static class DependencyInjection
{
    public static IServiceCollection AddAiService(this IServiceCollection collection, IConfiguration configuration)
    {
        collection.Configure<AiSettings>(configuration.GetSection("Ai"));
        collection.AddSingleton<AiSettings>(sp => sp.GetRequiredService<IOptions<AiSettings>>().Value);
        collection.AddChatClient(sp =>
            {
                var aiSettings = sp.GetRequiredService<AiSettings>();

                ArgumentException.ThrowIfNullOrWhiteSpace(aiSettings.Endpoint);
                ArgumentException.ThrowIfNullOrWhiteSpace(aiSettings.Model);

                var endpoint = new Uri(aiSettings.Endpoint);
                var isLocal = endpoint.IsLoopback ||
                              string.Equals(endpoint.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase);

                if (isLocal)
                {
                    aiSettings.ApiKey ??= "api-key";
                }
                else
                {
                    throw new InvalidOperationException(
                        $"ApiKey is required for remote endpoint '{endpoint.Host}'");
                }

                var openAiClient = new OpenAIClient(new ApiKeyCredential(aiSettings.ApiKey!),
                    new OpenAIClientOptions { Endpoint = new Uri(aiSettings.Endpoint) });
                return openAiClient.GetChatClient(aiSettings.Model).AsIChatClient();
            })
            .UseFunctionInvocation();

        collection.AddSingleton<IPromptProvider, FilePromptProvider>();
        collection.AddSingleton<IAiService, AiService>();
        return collection;
    }
}
