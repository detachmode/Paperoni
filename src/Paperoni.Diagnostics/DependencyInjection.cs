using Microsoft.Extensions.DependencyInjection;

namespace Paperoni.Diagnostics;

public static class DependencyInjection
{
    public static IServiceCollection AddDiagnostics(this IServiceCollection collection)
    {
        collection.AddSingleton<AlbumIdAccessor>();
        collection.AddSingleton<ILogRetriever, LogRetriever>();
        return collection;
    }
}
