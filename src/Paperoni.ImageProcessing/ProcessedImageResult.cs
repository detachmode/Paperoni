namespace Paperoni.ImageProcessing;

public record ProcessedImageResult
{
    public required byte[] ProcessedImage { get; init; }
    public bool DocumentDetected { get; init; }
    public TimeSpan ProcessingTime { get; init; }
    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }
}
