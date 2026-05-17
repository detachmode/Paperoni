using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Paperoni.Contract;

public static partial class Diagnostics
{
    public static readonly ActivitySource Tracer = new("Paperoni");

    [LoggerMessage(Level = LogLevel.Information, Message = "Album {MsgId} processing started (retry: {IsRetry})")]
    public static partial void ProcessingAlbum(this ILogger logger, int msgId, bool isRetry);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stage: AI summary starting for album {MsgId}")]
    public static partial void AiSummaryStarting(this ILogger logger, int msgId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stage: AI summary done for album {MsgId}")]
    public static partial void AiSummaryDone(this ILogger logger, int msgId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stage: PDF creation starting for album {MsgId}")]
    public static partial void PdfCreationStarting(this ILogger logger, int msgId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stage: PDF creation done for album {MsgId}")]
    public static partial void PdfCreationDone(this ILogger logger, int msgId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stage: Publishing markdown for album {MsgId}")]
    public static partial void PublishingMarkdown(this ILogger logger, int msgId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stage: Publishing PDF for album {MsgId}")]
    public static partial void PublishingPdf(this ILogger logger, int msgId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Stage: Cleaning up old files for album {MsgId}, title=\"{Title}\"")]
    public static partial void CleaningOldFiles(this ILogger logger, int msgId, string? title);

    [LoggerMessage(Level = LogLevel.Information, Message = "Album {MsgId} processing complete")]
    public static partial void AlbumComplete(this ILogger logger, int msgId);

    [LoggerMessage(Level = LogLevel.Error, Message = "An error occurred during processing album {MsgId}")]
    public static partial void AlbumProcessingError(this ILogger logger, Exception ex, int? msgId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Time to first AI chunk: {Ttf}ms")]
    public static partial void TimeToFirstChunk(this ILogger logger, long ttf);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI streaming done: {ChunkCount} chunks in {Latency:F1}s")]
    public static partial void AiStreamingDone(this ILogger logger, int chunkCount, double latency);

    [LoggerMessage(Level = LogLevel.Information, Message = "AI summary for album {MsgId}: {Length} chars, {Latency:F1}s, title=\"{Title}\"")]
    public static partial void AiSummaryCompleted(this ILogger logger, int msgId, int length, double latency, string title);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Processed {File}: document={Detected}, time={Time:F1}ms")]
    public static partial void ImageProcessed(this ILogger logger, string file, bool detected, double time);

    [LoggerMessage(Level = LogLevel.Information, Message = "PDF for album {MsgId}: {File} ({Size}KB, {Pages} pages, {Latency:F1}s)")]
    public static partial void PdfCreated(this ILogger logger, int msgId, string file, long size, int pages, double latency);

    [LoggerMessage(Level = LogLevel.Information, Message = "Published {Type} for album {MsgId}: {Source} -> {Dest} ({Size}KB)")]
    public static partial void FilePublished(this ILogger logger, string type, int msgId, string source, string dest, long size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted previous {Type}: {File}")]
    public static partial void FileDeleted(this ILogger logger, string type, string file);

    [LoggerMessage(Level = LogLevel.Information, Message = "Enqueued album {MsgId} (retry: {IsRetry}), queue depth: {Depth}")]
    public static partial void AlbumEnqueued(this ILogger logger, int msgId, bool isRetry, int depth);

    [LoggerMessage(Level = LogLevel.Information, Message = "Dequeued album {MsgId}, queue depth: {Depth}")]
    public static partial void AlbumDequeued(this ILogger logger, int msgId, int depth);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded {Name} ({Size} bytes)")]
    public static partial void FileDownloaded(this ILogger logger, string name, long size);

    [LoggerMessage(Level = LogLevel.Information, Message = "Downloaded {Count} file(s) for album {MsgId} ({Size} bytes total)")]
    public static partial void AlbumDownloaded(this ILogger logger, int count, int msgId, long size);

    [LoggerMessage(Level = LogLevel.Error, Message = "Error downloading album")]
    public static partial void DownloadError(this ILogger logger, Exception ex);
}
