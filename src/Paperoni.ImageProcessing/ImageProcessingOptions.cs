namespace Paperoni.ImageProcessing;

public enum ImageCorrectionMode
{
    AutoLevels,
    ShadowNormalized
}

public record ImageProcessingOptions
{
    public int MaxDimension { get; init; } = 2048;
    public int JpegQuality { get; init; } = 85;
    public ImageCorrectionMode CorrectionMode { get; init; } = ImageCorrectionMode.AutoLevels;
}
