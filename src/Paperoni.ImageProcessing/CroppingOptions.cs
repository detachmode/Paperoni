namespace Paperoni.ImageProcessing;

public enum CroppingMode
{
    Auto,
    Off,
    OpenCvOnly,
    ForceLlm
}

public enum CropDetectionConfidence
{
    High,
    Medium,
    Low
}

public enum CropStrategy
{
    NoCrop,
    OpenCv,
    Llm
}

public sealed record CroppingOptions
{
    public CroppingMode Mode { get; init; } = CroppingMode.Auto;
    public double HighConfidenceThreshold { get; init; } = 0.75;
    public double MediumConfidenceThreshold { get; init; } = 0.45;
    public int LlmTimeoutSeconds { get; init; } = 30;
    public int LlmMaxConcurrency { get; init; } = 2;
}
