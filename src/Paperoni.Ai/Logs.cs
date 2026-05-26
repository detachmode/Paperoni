using Microsoft.Extensions.Logging;

namespace Paperoni.Ai;

public static partial class Logs
{
    [LoggerMessage(Level = LogLevel.Error, Message = "❌ Processing failed for album {AlbumId}")]
    public static partial void AlbumProcessingError(this ILogger logger, Exception ex, int? albumId);

    [LoggerMessage(Level = LogLevel.Information, Message = "🤖 First chunk: {Ttf}ms")]
    public static partial void TimeToFirstChunk(this ILogger logger, long ttf);

    [LoggerMessage(Level = LogLevel.Information, Message = "🤖 Streaming done: {ChunkCount} chunks in {Latency:F1}s")]
    public static partial void AiStreamingDone(this ILogger logger, int chunkCount, double latency);

    [LoggerMessage(Level = LogLevel.Information, Message = "🤖 Summary: {Length} chars, {Latency:F1}s, title=\"{Title}\"")]
    public static partial void AiSummaryCompleted(this ILogger logger, int length, double latency, string title);

    [LoggerMessage(Level = LogLevel.Warning, Message = "🤖 AI response deserialization failed (attempt {Attempt}/{MaxRetries}): {Error}")]
    public static partial void AiRetryDeserialization(this ILogger logger, int attempt, int maxRetries, string error);

    [LoggerMessage(Level = LogLevel.Information, Message = "🤖 AI retry {Attempt}/{MaxRetries} — fixing response")]
    public static partial void AiRetryAttempt(this ILogger logger, int attempt, int maxRetries);
}
