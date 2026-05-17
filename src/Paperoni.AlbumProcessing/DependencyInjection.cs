using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Paperoni.Contract;
using Paperoni.Diagnostics;

namespace Paperoni.AlbumProcessing;

public static class DependencyInjection
{
    public static IServiceCollection AddAlbumProcessor(this IServiceCollection collection)
    {
        collection.AddHostedService<AlbumProcessor>();

        collection.AddKeyedSingleton<IFilePublisher, FilePublisher>(PublisherTarget.Markdown, (sp, _) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var workingDir = sp.GetRequiredService<AlbumWorkingDirectory>();
            var accessor = sp.GetRequiredService<AlbumIdAccessor>();
            var logger = sp.GetRequiredService<ILogger<FilePublisher>>();
            var outputPath = ResolveOutputPath(config, "MarkdownOutputPath");
            return new FilePublisher(workingDir, outputPath, "*.md", accessor, logger);
        });

        collection.AddKeyedSingleton<IFilePublisher, FilePublisher>(PublisherTarget.Pdf, (sp, _) =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var workingDir = sp.GetRequiredService<AlbumWorkingDirectory>();
            var accessor = sp.GetRequiredService<AlbumIdAccessor>();
            var logger = sp.GetRequiredService<ILogger<FilePublisher>>();
            var outputPath = ResolveOutputPath(config, "FilePublisherOutputPath");
            return new FilePublisher(workingDir, outputPath, "*.pdf", accessor, logger);
        });

        return collection;
    }

    private static string ResolveOutputPath(IConfiguration config, string normalKey)
    {
        if (bool.TryParse(config["TestMode"], out var testMode) && testMode)
        {
            return config["TestModeOutputPath"]
                ?? throw new InvalidOperationException("Configuration key 'TestModeOutputPath' is not set when TestMode is true");
        }
        return config[normalKey]
            ?? throw new InvalidOperationException($"Configuration key '{normalKey}' is not set");
    }
}
