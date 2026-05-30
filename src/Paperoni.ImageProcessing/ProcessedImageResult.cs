namespace Paperoni.ImageProcessing;

public record ProcessedImageResult
{
    public required byte[] ProcessedImage { get; init; }
    public bool DocumentDetected { get; init; }
    public TimeSpan ProcessingTime { get; init; }
    public int OriginalWidth { get; init; }
    public int OriginalHeight { get; init; }
    public CropStrategy CropStrategy { get; init; } = CropStrategy.NoCrop;
    public CropDetectionConfidence CropConfidence { get; init; } = CropDetectionConfidence.Low;
    public double CropScore { get; init; }
    public string CropStatus { get; init; } = "NoCrop";
}
