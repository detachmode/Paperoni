using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Paperoni.Ai;

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
            .Validate(settings => !string.IsNullOrWhiteSpace(settings.PdfOutputPath),
                "AlbumProcessing:PdfOutputPath is required")
            .Validate(settings => File.Exists(settings.ScriptFilePath),
                "AlbumProcessing:ScriptFilePath not found")
            .ValidateOnStart();

        collection.AddHostedService<AlbumProcessor>();

        collection.AddSingleton<AlbumProcessingSettings>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AlbumProcessingSettings>>();
            var settings = options.Value;
            Console.WriteLine("AlbumProcessing:");
            Console.WriteLine($"├─ MarkdownOutputPath: {settings.MarkdownOutputPath}");
            Console.WriteLine($"├─ PdfOutputPath: {settings.PdfOutputPath}");
            Console.WriteLine($"├─ ScriptFilePath: {settings.ScriptFilePath}");
            Console.WriteLine($"└─ TestMode: {settings.TestMode}");
            if (settings.TestMode)
            {
                Console.WriteLine($"   └─ TestModeOutputPath:{settings.TestModeOutputPath}");
            }

            return settings;
        });

        collection.AddSingleton<IScriptLoader, ScriptLoader>();

        collection.AddKeyedSingleton<IFilePublisher, FilePublisher>(PublisherTarget.Markdown, (sp, _) =>
        {
            var settings = sp.GetRequiredService<AlbumProcessingSettings>();
            var logger = sp.GetRequiredService<ILogger<FilePublisher>>();
            var outputPath = ResolveOutputPath(settings, settings.MarkdownOutputPath);
            return new FilePublisher(outputPath, logger);
        });

        collection.AddKeyedSingleton<IFilePublisher, FilePublisher>(PublisherTarget.Pdf, (sp, _) =>
        {
            var settings = sp.GetRequiredService<AlbumProcessingSettings>();
            var logger = sp.GetRequiredService<ILogger<FilePublisher>>();
            var outputPath = ResolveOutputPath(settings, settings.PdfOutputPath);
            return new FilePublisher(outputPath, logger);
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