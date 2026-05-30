using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OpenAI;

namespace Paperoni.Ai;

public static class DependencyInjection
{
    public static async Task ValidatePipelineScript(this IServiceProvider services)
    {
        var processingSettings = services.GetRequiredService<AiSettings>();
        await services.GetRequiredService<IScriptLoader>()
            .LoadAsync(processingSettings.ScriptFilePath, new ScriptGlobals([], DateTime.Now)
            {
                Logger = NullLogger.Instance,
                Log = Console.WriteLine
            });
    }

    public static IServiceCollection AddAiService(this IServiceCollection collection, IConfiguration configuration)
    {
        collection.AddOptions<AiSettings>()
            .Bind(configuration.GetSection("Ai"))
            .Validate(aiSettings => !string.IsNullOrWhiteSpace(aiSettings.Endpoint),
                "Ai:Endpoint is required")
            .Validate(aiSettings => !string.IsNullOrWhiteSpace(aiSettings.Model),
                "Ai:Model is required")
            .Validate(aiSettings => Uri.TryCreate(aiSettings.Endpoint, UriKind.Absolute, out _),
                "Ai:Endpoint is not a valid URI")
            .Validate(aiSettings =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(aiSettings.ScriptFilePath);
                File.ReadAllBytes(aiSettings.ScriptFilePath);
                return true;
            })
            .Validate(aiSettings =>
                {
                    if (!Uri.TryCreate(aiSettings.Endpoint, UriKind.Absolute, out var endpoint))
                    {
                        return true;
                    }

                    var isLocal = endpoint.IsLoopback ||
                                  string.Equals(endpoint.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(endpoint.Host, "host.docker.internal",
                                      StringComparison.OrdinalIgnoreCase);
                    return isLocal || !string.IsNullOrWhiteSpace(aiSettings.ApiKey);
                },
                "Ai:ApiKey is required when using a remote endpoint")
            .ValidateOnStart();

        collection.AddSingleton<AiSettings>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<AiSettings>>().Value;
            Console.WriteLine("Ai:");
            Console.WriteLine($"├─ Endpoint: {settings.Endpoint}");
            Console.WriteLine($"├─ Model: {settings.Model}");
            Console.WriteLine($"├─ ApiKey: {(!string.IsNullOrEmpty(settings.ApiKey) ? "***" : "not set")}");
            Console.WriteLine($"├─ Timeout: {settings.TimeoutSeconds} seconds");
            Console.WriteLine($"├─ ScriptFilePath: {settings.ScriptFilePath}");
            Console.WriteLine($"└─ MaxRetries: {settings.MaxRetries}");

            return settings;
        });
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
                    new OpenAIClientOptions
                    {
                        Endpoint = new Uri(aiSettings.Endpoint),
                        NetworkTimeout = TimeSpan.FromSeconds(aiSettings.TimeoutSeconds)
                    });
                return openAiClient.GetChatClient(aiSettings.Model).AsIChatClient();
            })
            .UseFunctionInvocation();

        collection.AddSingleton<IPipelineService, PipelineService>();
        return collection;
    }
}
