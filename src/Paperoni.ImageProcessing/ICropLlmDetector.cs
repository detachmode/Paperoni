namespace Paperoni.ImageProcessing;

public interface ICropLlmDetector
{
    Task<LlmCropResult> DetectAsync(byte[] imageData, CancellationToken cancellationToken = default);
}

public sealed record LlmCropResult
{
    public required bool Succeeded { get; init; }
    public required string RawResponse { get; init; }
    public string? Error { get; init; }
    public CropPoint[]? NormalizedCorners { get; init; }
    public ImageAdjustments? Adjustments { get; init; }
}
