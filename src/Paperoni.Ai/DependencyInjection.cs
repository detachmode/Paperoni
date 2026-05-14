using Microsoft.Extensions.DependencyInjection;

namespace Paperoni.Ai;

public static class DependencyInjection
{
    public static IServiceCollection AddAiService(this IServiceCollection collection)
    {
        collection.AddSingleton<IPromptProvider, FilePromptProvider>();
        collection.AddSingleton<IAiService, AiService>();
        return collection;
    }
}
