using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Paperoni.Contract;

namespace Paperoni.AlbumProcessing;

public static class DependencyInjection
{
    public static IServiceCollection AddAlbumProcessor(this IServiceCollection collection, IConfiguration configuration)
    {
        collection.AddOptions<AlbumProcessingSettings>()
            .Bind(configuration.GetSection("AlbumProcessing"))
            .Validate(settings => !settings.TestMode || !string.IsNullOrWhiteSpace(settings.TestModeOutputPath),
                "AlbumProcessing:TestModeOutputPath is required when AlbumProcessing:TestMode is true")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.MarkdownOutputPath),
                "AlbumProcessing:MarkdownOutputPath is required")
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.FilePublisherOutputPath),
                "AlbumProcessing:FilePublisherOutputPath is required")
            .ValidateOnStart();

        collection.AddHostedService<AlbumProcessor>();

        collection.AddSingleton<AlbumProcessingSettings>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AlbumProcessingSettings>>();
            return options.Value;
        });

        collection.AddKeyedSingleton<IFilePublisher, FilePublisher>(PublisherTarget.Markdown, (sp, _) =>
        {
            var settings = sp.GetRequiredService<AlbumProcessingSettings>();
            var workingDir = sp.GetRequiredService<AlbumWorkingDirectory>();
            var logger = sp.GetRequiredService<ILogger<FilePublisher>>();
            var outputPath = ResolveOutputPath(settings, settings.MarkdownOutputPath);
            return new FilePublisher(workingDir, outputPath, "*.md", logger);
        });

        collection.AddKeyedSingleton<IFilePublisher, FilePublisher>(PublisherTarget.Pdf, (sp, _) =>
        {
            var settings = sp.GetRequiredService<AlbumProcessingSettings>();
            var workingDir = sp.GetRequiredService<AlbumWorkingDirectory>();
            var logger = sp.GetRequiredService<ILogger<FilePublisher>>();
            var outputPath = ResolveOutputPath(settings, settings.FilePublisherOutputPath);
            return new FilePublisher(workingDir, outputPath, "*.pdf", logger);
        });

        return collection;
    }

    private static string ResolveOutputPath(AlbumProcessingSettings settings, string? normalPath)
    {
        if (settings.TestMode)
        {
            return settings.TestModeOutputPath!;
        }

        return normalPath!;
    }
}
