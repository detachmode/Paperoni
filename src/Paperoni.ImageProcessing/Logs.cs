using Microsoft.Extensions.Logging;

namespace Paperoni.ImageProcessing;

public static partial class Logs
{
    [LoggerMessage(Level = LogLevel.Debug, Message = "🖼️ Processed {File}: document={Detected}, {Time:F1}ms")]
    public static partial void ImageProcessed(this ILogger logger, string file, bool detected, double time);

    [LoggerMessage(Level = LogLevel.Information, Message = "✂️ Crop {File}: {Status}")]
    public static partial void CropProcessed(this ILogger logger, string file, string status);

    [LoggerMessage(Level = LogLevel.Information, Message = "📄 PDF: {File} ({Size}KB, {Pages} pages, {Latency:F1}s)")]
    public static partial void PdfCreated(this ILogger logger, string file, long size, int pages, double latency);
}
