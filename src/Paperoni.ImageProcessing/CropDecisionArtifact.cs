namespace Paperoni.ImageProcessing;

public sealed record CropDecisionArtifact
{
    public required string SourceFile { get; init; }
    public required CroppingMode Mode { get; init; }
    public required CropStrategy FinalStrategy { get; init; }
    public required string Reason { get; init; }
    public required OpenCvCropEvidence OpenCv { get; init; }
    public LlmCropEvidence? Llm { get; init; }
}

public sealed record OpenCvCropEvidence
{
    public required bool Detected { get; init; }
    public required CropDetectionConfidence Confidence { get; init; }
    public required double Score { get; init; }
    public required double AreaRatio { get; init; }
    public required double EdgeMargin { get; init; }
    public required double Rectangularity { get; init; }
    public required double AspectPlausibility { get; init; }
    public required string Reason { get; init; }
    public CropPoint[]? Corners { get; init; }
}

public sealed record LlmCropEvidence
{
    public required bool Attempted { get; init; }
    public required bool Succeeded { get; init; }
    public required string Reason { get; init; }
    public CropPoint[]? Corners { get; init; }
    public ImageAdjustments? Adjustments { get; init; }
    public string? RawResponseFile { get; init; }
}

public sealed record CropPoint(double X, double Y);

public sealed record ImageAdjustments(double Brightness = 0, double Contrast = 1.0, double Gamma = 1.0);
