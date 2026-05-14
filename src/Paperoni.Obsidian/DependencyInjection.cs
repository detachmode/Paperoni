using Microsoft.Extensions.DependencyInjection;

namespace Paperoni.Obsidian;

public static class DependencyInjection
{
    public static IServiceCollection AddObsidianStore(this IServiceCollection collection)
    {
        collection.AddSingleton<IObsidianStore, ObsidianStore>();
        return collection;
    }
    
}