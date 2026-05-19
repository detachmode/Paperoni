using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Paperoni.Contract;

namespace Paperoni.Diagnostics;

public static class DependencyInjection
{
    public static IServiceCollection AddDiagnostics(this IServiceCollection collection, IConfiguration configuration)
    {
        collection.AddOptions<DiagnosticsSettings>()
            .Bind(configuration.GetSection("Diagnostics"))
            .ValidateOnStart();
        collection.AddSingleton<DiagnosticsSettings>(sp =>
        {
            var settings = sp.GetRequiredService<IOptions<DiagnosticsSettings>>().Value;
            var workingDirectory = sp.GetRequiredService<AlbumWorkingDirectory>();
            if (string.IsNullOrWhiteSpace(settings.LogPath))
            {
                settings.LogPath = workingDirectory.BasePath;
            }
            Console.WriteLine($"Diagnostics:");
            Console.WriteLine($"  LogPath={settings.LogPath}");
            return settings;
        });

        collection.AddSingleton<AlbumIdAccessor>();
        collection.AddSingleton<ILogRetriever, LogRetriever>();
        return collection;
    }
}
