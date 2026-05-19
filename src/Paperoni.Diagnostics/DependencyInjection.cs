using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Paperoni.Diagnostics;

public static class DependencyInjection
{
    public static IServiceCollection AddDiagnostics(this IServiceCollection collection, IConfiguration configuration)
    {
        collection.AddOptions<DiagnosticsSettings>()
            .Bind(configuration.GetSection("Diagnostics"))
            .ValidateOnStart();
        collection.PostConfigure<DiagnosticsSettings>(settings =>
        {
            Console.WriteLine($"Diagnostics: LogPath={settings.LogPath ?? "(default)"}");
        });
        collection.AddSingleton<DiagnosticsSettings>(sp =>
            sp.GetRequiredService<IOptions<DiagnosticsSettings>>().Value);

        collection.AddSingleton<AlbumIdAccessor>();
        collection.AddSingleton<ILogRetriever, LogRetriever>();
        return collection;
    }
}
