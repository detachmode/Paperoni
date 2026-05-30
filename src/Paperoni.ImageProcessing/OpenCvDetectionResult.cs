using OpenCvSharp;

namespace Paperoni.ImageProcessing;

internal sealed record OpenCvDetectionResult(
    int OriginalWidth,
    int OriginalHeight,
    Point2f[]? Corners,
    CropDetectionConfidence Confidence,
    double Score,
    double AreaRatio,
    double EdgeMargin,
    double Rectangularity,
    double AspectPlausibility,
    string Reason)
{
    public static OpenCvDetectionResult None(int originalWidth, int originalHeight, string reason) => new(
        originalWidth,
        originalHeight,
        null,
        CropDetectionConfidence.Low,
        0,
        0,
        0,
        0,
        0,
        reason);
}

internal sealed record CropMetrics(
    double AreaRatio,
    double EdgeMargin,
    double Rectangularity,
    double AspectPlausibility);
