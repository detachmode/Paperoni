namespace Paperoni;

public static partial class Logs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "📥 Processing started (retry: {IsRetry})")]
    public static partial void ProcessingAlbum(this ILogger logger, bool isRetry);

    [LoggerMessage(Level = LogLevel.Information, Message = "🤖 AI summary started")]
    public static partial void AiSummaryStarting(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "🤖 AI summary done")]
    public static partial void AiSummaryDone(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "📄 PDF started")]
    public static partial void PdfCreationStarting(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "📄 PDF done")]
    public static partial void PdfCreationDone(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "📤 Publishing Markdown")]
    public static partial void PublishingMarkdown(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "📤 Publishing PDF")]
    public static partial void PublishingPdf(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "🧹 Cleaning old files: {Title}")]
    public static partial void CleaningOldFiles(this ILogger logger, string? title);

    [LoggerMessage(Level = LogLevel.Information, Message = "✅ Processing complete")]
    public static partial void AlbumComplete(this ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "📤 Published {Type}: {Source} -> {Dest} ({Size}KB)")]
    public static partial void FilePublished(this ILogger logger, string type, string source, string dest, long size);

    [LoggerMessage(Level = LogLevel.Information, Message = "🗑️ Deleted previous {Type}: {File}")]
    public static partial void FileDeleted(this ILogger logger, string type, string file);

    [LoggerMessage(Level = LogLevel.Debug, Message = "🧹 Deleted working directory for album {AlbumId}")]
    public static partial void WorkingDirectoryDeleted(this ILogger logger, int albumId);

    [LoggerMessage(Level = LogLevel.Information, Message = "🧹 Working directory cleanup: deleted {Count} directories older than {RetentionDays} days")]
    public static partial void WorkingDirectoryCleanupComplete(this ILogger logger, int count, int retentionDays);
}
