using Microsoft.Extensions.Logging;

namespace Paperoni.Telegram;

public static partial class Logs
{
    [LoggerMessage(Level = LogLevel.Information, Message = "⬇️ Downloaded {Name} ({Size} bytes)")]
    public static partial void FileDownloaded(this ILogger logger, string name, long size);

    [LoggerMessage(Level = LogLevel.Information, Message = "⬇️ Downloaded {Count} files for album {AlbumId} ({Size} bytes)")]
    public static partial void AlbumDownloaded(this ILogger logger, int count, int albumId, long size);

    [LoggerMessage(Level = LogLevel.Error, Message = "❌ Download failed")]
    public static partial void DownloadError(this ILogger logger, Exception ex);
}
