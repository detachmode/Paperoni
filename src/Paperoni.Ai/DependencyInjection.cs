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
        collection.AddOptions<AiSettings>()
            .Bind(configuration.GetSection("Ai"))
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Endpoint),
                "Ai:Endpoint is required")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.Model),
                "Ai:Model is required")
            .Validate(settings => Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out _),
                "Ai:Endpoint is not a valid URI")
            .Validate(settings =>
                {
                    if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpoint))
                    {
                        return true;
                    }

                    var isLocal = endpoint.IsLoopback ||
                                  string.Equals(endpoint.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(endpoint.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase);
                    return isLocal || !string.IsNullOrWhiteSpace(settings.ApiKey);
                },
                "Ai:ApiKey is required when using a remote endpoint")
            .ValidateOnStart();
        collection.PostConfigure<AiSettings>(settings => { settings.ApiKey ??= configuration["AI_API_KEY"]; });
        collection.AddSingleton<AiSettings>(sp => sp.GetRequiredService<IOptions<AiSettings>>().Value);
        collection.AddChatClient(sp =>
            {
                var aiSettings = sp.GetRequiredService<AiSettings>();

                var endpoint = new Uri(aiSettings.Endpoint);
                var isLocal = endpoint.IsLoopback ||
                              string.Equals(endpoint.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                              string.Equals(endpoint.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase);

                if (isLocal)
                {
                    aiSettings.ApiKey ??= "api-key";
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
