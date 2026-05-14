namespace Paperoni.ImageProcessing;

public record ImageProcessingOptions
{
    public int MaxDimension { get; init; } = 2048;
    public int JpegQuality { get; init; } = 85;
}
