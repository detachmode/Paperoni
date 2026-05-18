using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Paperoni.Diagnostics;

public static class DependencyInjection
{
    public static IServiceCollection AddDiagnostics(this IServiceCollection collection, IConfiguration configuration)
    {
        collection.Configure<DiagnosticsSettings>(configuration.GetSection("Diagnostics"));
        collection.AddSingleton<DiagnosticsSettings>(sp =>
            sp.GetRequiredService<IOptions<DiagnosticsSettings>>().Value);

        collection.AddSingleton<AlbumIdAccessor>();
        collection.AddSingleton<ILogRetriever, LogRetriever>();
        return collection;
    }
}
