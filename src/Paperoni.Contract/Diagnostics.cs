using Microsoft.Extensions.Logging;

namespace Paperoni.Contract;

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

    [LoggerMessage(Level = LogLevel.Error, Message = "❌ Processing failed for album {AlbumId}")]
    public static partial void AlbumProcessingError(this ILogger logger, Exception ex, int? albumId);

    [LoggerMessage(Level = LogLevel.Information, Message = "🤖 First chunk: {Ttf}ms")]
    public static partial void TimeToFirstChunk(this ILogger logger, long ttf);

    [LoggerMessage(Level = LogLevel.Information, Message = "🤖 Streaming done: {ChunkCount} chunks in {Latency:F1}s")]
    public static partial void AiStreamingDone(this ILogger logger, int chunkCount, double latency);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "🤖 Summary: {Length} chars, {Latency:F1}s, title=\"{Title}\"")]
    public static partial void AiSummaryCompleted(this ILogger logger, int length, double latency, string title);

    [LoggerMessage(Level = LogLevel.Debug, Message = "🖼️ Processed {File}: document={Detected}, {Time:F1}ms")]
    public static partial void ImageProcessed(this ILogger logger, string file, bool detected, double time);

    [LoggerMessage(Level = LogLevel.Information, Message = "📄 PDF: {File} ({Size}KB, {Pages} pages, {Latency:F1}s)")]
    public static partial void PdfCreated(this ILogger logger, string file, long size, int pages, double latency);

    [LoggerMessage(Level = LogLevel.Information, Message = "📤 Published {Type}: {Source} -> {Dest} ({Size}KB)")]
    public static partial void FilePublished(this ILogger logger, string type, string source, string dest, long size);

    [LoggerMessage(Level = LogLevel.Information, Message = "🗑️ Deleted previous {Type}: {File}")]
    public static partial void FileDeleted(this ILogger logger, string type, string file);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "📥 Enqueued album {AlbumId} (retry: {IsRetry}), depth: {Depth}")]
    public static partial void AlbumEnqueued(this ILogger logger, int albumId, bool isRetry, int depth);

    [LoggerMessage(Level = LogLevel.Information, Message = "📥 Dequeued album {AlbumId}, depth: {Depth}")]
    public static partial void AlbumDequeued(this ILogger logger, int albumId, int depth);

    [LoggerMessage(Level = LogLevel.Information, Message = "⬇️ Downloaded {Name} ({Size} bytes)")]
    public static partial void FileDownloaded(this ILogger logger, string name, long size);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "⬇️ Downloaded {Count} files for album {AlbumId} ({Size} bytes)")]
    public static partial void AlbumDownloaded(this ILogger logger, int count, int albumId, long size);

    [LoggerMessage(Level = LogLevel.Error, Message = "❌ Download failed")]
    public static partial void DownloadError(this ILogger logger, Exception ex);

    [LoggerMessage(Level = LogLevel.Debug, Message = "🧹 Deleted working directory for album {AlbumId}")]
    public static partial void WorkingDirectoryDeleted(this ILogger logger, int albumId);

    [LoggerMessage(Level = LogLevel.Information,
        Message = "🧹 Working directory cleanup: deleted {Count} directories older than {RetentionDays} days")]
    public static partial void WorkingDirectoryCleanupComplete(this ILogger logger, int count, int retentionDays);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "🤖 AI response deserialization failed (attempt {Attempt}/{MaxRetries}): {Error}")]
    public static partial void AiRetryDeserialization(this ILogger logger, int attempt, int maxRetries, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "🤖 AI retry {Attempt}/{MaxRetries} — fixing response")]
    public static partial void AiRetryAttempt(this ILogger logger, int attempt, int maxRetries);
}
