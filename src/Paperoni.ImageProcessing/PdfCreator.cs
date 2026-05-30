using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Paperoni.Contract;
using static Paperoni.Diagnostics.ActivityExtensions;

namespace Paperoni.ImageProcessing;

public record AutoCorrectImageResult(
    byte[] ImprovedImage,
    string OriginalImagePath
);

internal sealed class PdfCreator(
    ILogger<PdfCreator> logger,
    WorkingDirectory workingDirectory,
    CroppingOptions croppingOptions,
    ICropLlmDetector llmCropDetector) : IPdfCreator
{
    private static readonly ImageProcessingOptions s_defaultOptions = new();
    private static readonly JsonSerializerOptions s_jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    static PdfCreator()
    {
        s_jsonOptions.Converters.Add(new JsonStringEnumConverter());
    }

    public async Task CreatePdf(int messageId, Func<string, Task>? statusCallback = null, bool forceLlmCrop = false,
        CancellationToken cancellationToken = default)
    {
        await Tracer.TraceAsync<PdfCreator>(async scope =>
        {
            var sw = Stopwatch.StartNew();

            var downloadPath = workingDirectory.RequireWorkingDirectory(messageId);
            var aiResult = await workingDirectory.GetData<PipelineResult>(messageId, cancellationToken);
            ArgumentNullException.ThrowIfNull(aiResult);

            var files = Directory.GetFiles(downloadPath);
            var originalImages = files.Where(FileHelpers.IsImageFile).OrderBy(Path.GetFileName).ToList();
            scope.SetTag("imageCount", originalImages.Count);
            scope.SetTag("title", aiResult.Filename);

            var processedData = await AutoCorrect(originalImages, downloadPath, statusCallback, forceLlmCrop, cancellationToken);

            var pdfBytes = PdfMerger.MergeToPdf(processedData.Select(i => i.ImprovedImage));
            var pdfPath = Path.Combine(downloadPath, $"{aiResult.Filename}.pdf");

            await File.WriteAllBytesAsync(pdfPath, pdfBytes, cancellationToken);
            sw.Stop();

            scope.SetTag("file", Path.GetFileName(pdfPath));
            scope.SetTag("sizeKb", pdfBytes.Length / 1024);
            scope.SetTag("pages", originalImages.Count);
            scope.SetTag("latencySec", sw.Elapsed.TotalSeconds);

            logger.PdfCreated(Path.GetFileName(pdfPath), pdfBytes.Length / 1024,
                originalImages.Count, sw.Elapsed.TotalSeconds);
        });
    }

    public static Task<ProcessedImageResult> AutoCorrect(byte[] imageData, CancellationToken ct = default)
        => AutoCorrect(imageData, s_defaultOptions, ct);

    public async Task<List<AutoCorrectImageResult>> AutoCorrect(List<string> originalImages, string workingDirectoryPath,
        Func<string, Task>? statusCallback, bool forceLlmCrop, CancellationToken cancellationToken)
    {
        var mode = forceLlmCrop ? CroppingMode.ForceLlm : croppingOptions.Mode;
        using var llmSemaphore = new SemaphoreSlim(Math.Max(1, croppingOptions.LlmMaxConcurrency));

        var tasks = originalImages.Select((imageFile, index) => Tracer.TraceAsync<PdfCreator, AutoCorrectImageResult>(async scope =>
        {
            var fileName = Path.GetFileName(imageFile);
            scope.SetTag("file", fileName);
            scope.SetTag("croppingMode", mode);

            var imageData = await File.ReadAllBytesAsync(imageFile, cancellationToken);
            var processed = await AutoCorrectWithRouting(imageData, imageFile, workingDirectoryPath, mode, llmSemaphore,
                cancellationToken);

            scope.SetTag("documentDetected", processed.DocumentDetected);
            scope.SetTag("cropStrategy", processed.CropStrategy);
            scope.SetTag("cropConfidence", processed.CropConfidence);
            scope.SetTag("cropScore", processed.CropScore);
            scope.SetTag("processingTimeMs", processed.ProcessingTime.TotalMilliseconds);

            var status = $"✂️ Photo {index + 1}: {processed.CropStatus}";
            if (statusCallback is not null)
            {
                await statusCallback(status);
            }

            logger.ImageProcessed(fileName, processed.DocumentDetected, processed.ProcessingTime.TotalMilliseconds);
            logger.CropProcessed(fileName, processed.CropStatus);

            return new AutoCorrectImageResult(processed.ProcessedImage, imageFile);
        })).ToArray();

        return [.. await Task.WhenAll(tasks)];
    }

    public static async Task<ProcessedImageResult> AutoCorrect(byte[] imageData, ImageProcessingOptions options,
        CancellationToken ct = default)
        => await Task.Run(() => ProcessOpenCvOnly(imageData, options), ct);

    private async Task<ProcessedImageResult> AutoCorrectWithRouting(byte[] imageData, string imageFile,
        string workingDirectoryPath, CroppingMode mode, SemaphoreSlim llmSemaphore, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var openCv = DetectOpenCvCrop(imageData, croppingOptions);
        var finalStrategy = CropStrategy.NoCrop;
        var reason = "Cropping disabled";
        LlmCropEvidence? llmEvidence = null;
        byte[] processed;

        if (mode == CroppingMode.Off)
        {
            processed = ApplyNoCrop(imageData, s_defaultOptions.JpegQuality);
        }
        else if (mode == CroppingMode.OpenCvOnly)
        {
            if (openCv.Corners is not null)
            {
                processed = ApplyCrop(imageData, openCv.Corners, s_defaultOptions.JpegQuality, histogramLevels: true);
                finalStrategy = CropStrategy.OpenCv;
                reason = "OpenCV-only mode";
            }
            else
            {
                processed = ApplyNoCrop(imageData, s_defaultOptions.JpegQuality);
                reason = openCv.Reason;
            }
        }
        else if (mode == CroppingMode.ForceLlm || openCv.Confidence != CropDetectionConfidence.High)
        {
            var llm = await RunLlmCrop(imageData, imageFile, workingDirectoryPath, llmSemaphore, cancellationToken);
            llmEvidence = llm.Evidence;
            if (llm.Result.Succeeded && llm.Result.NormalizedCorners is not null)
            {
                var pixelCorners = ToPixelCorners(llm.Result.NormalizedCorners, openCv.OriginalWidth, openCv.OriginalHeight);
                try
                {
                    processed = ApplyCrop(imageData, pixelCorners, s_defaultOptions.JpegQuality, histogramLevels: true,
                        brightness: llm.Result.Adjustments?.Brightness ?? 0,
                        contrast: llm.Result.Adjustments?.Contrast ?? 1.0,
                        gamma: llm.Result.Adjustments?.Gamma ?? 1.0);
                    finalStrategy = CropStrategy.Llm;
                    reason = mode == CroppingMode.ForceLlm ? "Forced LLM crop" : $"OpenCV {openCv.Confidence} confidence";
                }
                catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                {
                    processed = ApplyNoCrop(imageData, s_defaultOptions.JpegQuality);
                    finalStrategy = CropStrategy.NoCrop;
                    reason = $"LLM crop invalid: {ex.Message}";
                    llmEvidence = llm.Evidence with { Succeeded = false, Reason = reason };
                }
            }
            else
            {
                processed = ApplyNoCrop(imageData, s_defaultOptions.JpegQuality);
                reason = llm.Result.Error ?? "LLM crop failed";
            }
        }
        else
        {
            processed = ApplyCrop(imageData, openCv.Corners!, s_defaultOptions.JpegQuality, histogramLevels: true);
            finalStrategy = CropStrategy.OpenCv;
            reason = "OpenCV high confidence";
        }

        sw.Stop();
        await WriteCropDecision(imageFile, workingDirectoryPath, mode, finalStrategy, reason, openCv, llmEvidence,
            cancellationToken);

        return new ProcessedImageResult
        {
            ProcessedImage = processed,
            DocumentDetected = finalStrategy is CropStrategy.OpenCv or CropStrategy.Llm,
            ProcessingTime = sw.Elapsed,
            OriginalWidth = openCv.OriginalWidth,
            OriginalHeight = openCv.OriginalHeight,
            CropStrategy = finalStrategy,
            CropConfidence = openCv.Confidence,
            CropScore = openCv.Score,
            CropStatus = FormatCropStatus(openCv, finalStrategy, reason, llmEvidence)
        };
    }

    private async Task<(LlmCropResult Result, LlmCropEvidence Evidence)> RunLlmCrop(byte[] imageData, string imageFile,
        string workingDirectoryPath, SemaphoreSlim semaphore, CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(croppingOptions.LlmTimeoutSeconds));

            LlmCropResult result;
            try
            {
                var llmImageData = ResizeForLlmCrop(imageData, croppingOptions.LlmMaxDimension);
                result = await llmCropDetector.DetectAsync(llmImageData, timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = new LlmCropResult
                {
                    Succeeded = false,
                    RawResponse = string.Empty,
                    Error = $"LLM crop timed out after {croppingOptions.LlmTimeoutSeconds}s"
                };
            }

            var rawResponseFile = await WriteRawLlmResponse(imageFile, workingDirectoryPath, result.RawResponse,
                cancellationToken);
            var evidence = new LlmCropEvidence
            {
                Attempted = true,
                Succeeded = result.Succeeded,
                Reason = result.Succeeded ? "LLM crop returned valid corners" : result.Error ?? "LLM crop failed",
                Corners = result.NormalizedCorners,
                Adjustments = result.Adjustments,
                RawResponseFile = rawResponseFile
            };

            return (result, evidence);
        }
        finally
        {
            semaphore.Release();
        }
    }

    public static byte[] ApplyCrop(byte[] imageData, Point2f[] corners, int jpegQuality = 85,
        bool histogramLevels = false,
        int? denoiseD = null, double denoiseSigmaColor = 75, double denoiseSigmaSpace = 75,
        double brightness = 0, double contrast = 1.0, double gamma = 1.0)
    {
        if (corners.Length != 4)
        {
            throw new ArgumentException("Exactly 4 corners are required", nameof(corners));
        }

        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);

        var ordered = OrderCorners(corners);
        var destSize = ComputeDestSize(ordered);

        if (destSize.Width < 200 || destSize.Height < 200)
        {
            throw new InvalidOperationException(
                $"Crop result too small: {destSize.Width}x{destSize.Height} (minimum 200x200)");
        }

        var dstPoints = new[]
        {
            new Point2f(0, 0), new Point2f(destSize.Width - 1, 0),
            new Point2f(destSize.Width - 1, destSize.Height - 1), new Point2f(0, destSize.Height - 1)
        };

        var transform = Cv2.GetPerspectiveTransform(ordered, dstPoints);
        using var warped = new Mat();
        Cv2.WarpPerspective(src, warped, transform, destSize);

        if (denoiseD.HasValue)
        {
            ApplyBilateralFilter(warped, denoiseD.Value, denoiseSigmaColor, denoiseSigmaSpace);
        }

        ApplyAdjustments(warped, brightness, contrast, gamma);

        using var gray = new Mat();
        if (warped.Channels() == 3)
        {
            Cv2.CvtColor(warped, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            warped.CopyTo(gray);
        }

        if (histogramLevels)
        {
            AutoLevels(gray);
        }

        Cv2.ImEncode(".jpg", gray, out var encoded,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality));

        return encoded;
    }

    private static ProcessedImageResult ProcessOpenCvOnly(byte[] imageData, ImageProcessingOptions options)
    {
        var sw = Stopwatch.StartNew();

        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        var originalWidth = src.Width;
        var originalHeight = src.Height;

        // Speed up edge detection by working at ~1200px
        using var small = ResizeForProcessing(src);
        var scaleX = (double)src.Width / small.Width;
        var scaleY = (double)src.Height / small.Height;

        using var gray = new Mat();
        Cv2.CvtColor(small, gray, ColorConversionCodes.BGR2GRAY);

        // Heavy blur to suppress text, then Otsu to find the document region
        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(31, 31), 0);

        using var binary = new Mat();
        Cv2.Threshold(blurred, binary, 0, 255, ThresholdTypes.Otsu);

        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

        var imageArea = small.Width * small.Height;
        var minArea = imageArea * 0.05;

        var bestContour = contours
            .Select(c => new { Contour = c, Area = Cv2.ContourArea(c) })
            .Where(x => x.Area > minArea)
            .OrderByDescending(x => x.Area)
            .FirstOrDefault();

        Mat? warped = null;
        var documentDetected = false;

        if (bestContour != null)
        {
            var perimeter = Cv2.ArcLength(bestContour.Contour, true);
            var approx = Cv2.ApproxPolyDP(bestContour.Contour, 0.02 * perimeter, true);

            if (approx.Length == 4)
            {
                var corners = approx.Select(p => new Point2f(p.X * (float)scaleX, p.Y * (float)scaleY)).ToArray();
                corners = OrderCorners(corners);

                var destSize = ComputeDestSize(corners);
                if (destSize.Width >= 200 && destSize.Height >= 200)
                {
                    var dstPoints = new[]
                    {
                        new Point2f(0, 0), new Point2f(destSize.Width - 1, 0),
                        new Point2f(destSize.Width - 1, destSize.Height - 1), new Point2f(0, destSize.Height - 1)
                    };

                    var transform = Cv2.GetPerspectiveTransform(corners, dstPoints);
                    warped = new Mat();
                    Cv2.WarpPerspective(src, warped, transform, destSize);
                    documentDetected = true;
                }
            }
        }

        using var toProcess = warped ?? src;

        using var output = new Mat();
        if (toProcess.Channels() == 3)
        {
            Cv2.CvtColor(toProcess, output, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            toProcess.CopyTo(output);
        }

        AutoLevels(output);

        Cv2.ImEncode(".jpg", output, out var encoded,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, options.JpegQuality));

        sw.Stop();
        return new ProcessedImageResult
        {
            ProcessedImage = encoded,
            DocumentDetected = documentDetected,
            ProcessingTime = sw.Elapsed,
            OriginalWidth = originalWidth,
            OriginalHeight = originalHeight,
            CropStrategy = documentDetected ? CropStrategy.OpenCv : CropStrategy.NoCrop,
            CropConfidence = documentDetected ? CropDetectionConfidence.High : CropDetectionConfidence.Low,
            CropScore = documentDetected ? 1 : 0,
            CropStatus = documentDetected ? "OpenCV crop" : "NoCrop"
        };
    }

    private static OpenCvDetectionResult DetectOpenCvCrop(byte[] imageData, CroppingOptions options)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        var originalWidth = src.Width;
        var originalHeight = src.Height;

        using var small = ResizeForProcessing(src);
        var scaleX = (double)src.Width / small.Width;
        var scaleY = (double)src.Height / small.Height;

        using var gray = new Mat();
        Cv2.CvtColor(small, gray, ColorConversionCodes.BGR2GRAY);

        using var blurred = new Mat();
        Cv2.GaussianBlur(gray, blurred, new Size(31, 31), 0);

        using var binary = new Mat();
        Cv2.Threshold(blurred, binary, 0, 255, ThresholdTypes.Otsu);

        Cv2.FindContours(binary, out var contours, out _, RetrievalModes.List, ContourApproximationModes.ApproxSimple);

        var imageArea = small.Width * small.Height;
        var minArea = imageArea * 0.05;
        var bestContour = contours
            .Select(c => new { Contour = c, Area = Cv2.ContourArea(c) })
            .Where(x => x.Area > minArea)
            .OrderByDescending(x => x.Area)
            .FirstOrDefault();

        if (bestContour is null)
        {
            return OpenCvDetectionResult.None(originalWidth, originalHeight, "No contour above minimum area");
        }

        var perimeter = Cv2.ArcLength(bestContour.Contour, true);
        var approx = Cv2.ApproxPolyDP(bestContour.Contour, 0.02 * perimeter, true);
        if (approx.Length != 4)
        {
            return OpenCvDetectionResult.None(originalWidth, originalHeight,
                $"Best contour has {approx.Length} corners, expected 4");
        }

        var corners = approx.Select(p => new Point2f(p.X * (float)scaleX, p.Y * (float)scaleY)).ToArray();
        corners = OrderCorners(corners);

        var destSize = ComputeDestSize(corners);
        if (destSize.Width < 200 || destSize.Height < 200)
        {
            return OpenCvDetectionResult.None(originalWidth, originalHeight,
                $"Crop result too small: {destSize.Width}x{destSize.Height}");
        }

        var metrics = ComputeMetrics(corners, originalWidth, originalHeight, destSize);
        var score = Math.Clamp(
            metrics.AreaRatio * 0.35 +
            metrics.EdgeMargin * 0.20 +
            metrics.Rectangularity * 0.30 +
            metrics.AspectPlausibility * 0.15,
            0,
            1);
        var confidence = score >= options.HighConfidenceThreshold
            ? CropDetectionConfidence.High
            : score >= options.MediumConfidenceThreshold
                ? CropDetectionConfidence.Medium
                : CropDetectionConfidence.Low;

        return new OpenCvDetectionResult(
            originalWidth,
            originalHeight,
            corners,
            confidence,
            score,
            metrics.AreaRatio,
            metrics.EdgeMargin,
            metrics.Rectangularity,
            metrics.AspectPlausibility,
            "OpenCV found a four-corner crop");
    }

    private static CropMetrics ComputeMetrics(Point2f[] corners, int width, int height, Size destSize)
    {
        var areaRatio = PolygonArea(corners) / (width * (double)height);
        var edgeMarginPixels = corners.Min(p => Math.Min(Math.Min(p.X, width - p.X), Math.Min(p.Y, height - p.Y)));
        var edgeMargin = Math.Clamp(edgeMarginPixels / Math.Min(width, height) * 4, 0, 1);
        var rectangularity = ComputeRectangularity(corners);
        var aspect = destSize.Width / (double)destSize.Height;
        var aspectPlausibility = aspect is >= 0.35 and <= 3.0 ? 1.0 : Math.Clamp(1.0 / Math.Abs(aspect), 0, 1);
        return new CropMetrics(areaRatio, edgeMargin, rectangularity, aspectPlausibility);
    }

    private static double PolygonArea(Point2f[] corners)
    {
        var area = 0.0;
        for (var i = 0; i < corners.Length; i++)
        {
            var j = (i + 1) % corners.Length;
            area += corners[i].X * corners[j].Y - corners[j].X * corners[i].Y;
        }

        return Math.Abs(area) / 2.0;
    }

    private static double ComputeRectangularity(Point2f[] corners)
    {
        var totalDeviation = 0.0;
        for (var i = 0; i < corners.Length; i++)
        {
            var prev = corners[(i + corners.Length - 1) % corners.Length];
            var current = corners[i];
            var next = corners[(i + 1) % corners.Length];
            var angle = Angle(prev, current, next);
            totalDeviation += Math.Abs(90 - angle);
        }

        return Math.Clamp(1 - totalDeviation / (45 * corners.Length), 0, 1);
    }

    private static double Angle(Point2f a, Point2f b, Point2f c)
    {
        var abx = a.X - b.X;
        var aby = a.Y - b.Y;
        var cbx = c.X - b.X;
        var cby = c.Y - b.Y;
        var dot = abx * cbx + aby * cby;
        var mag1 = Math.Sqrt(abx * abx + aby * aby);
        var mag2 = Math.Sqrt(cbx * cbx + cby * cby);
        if (mag1 == 0 || mag2 == 0)
        {
            return 0;
        }

        return Math.Acos(Math.Clamp(dot / (mag1 * mag2), -1, 1)) * 180 / Math.PI;
    }

    private static byte[] ApplyNoCrop(byte[] imageData, int jpegQuality)
    {
        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        using var gray = new Mat();
        if (src.Channels() == 3)
        {
            Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            src.CopyTo(gray);
        }

        AutoLevels(gray);
        Cv2.ImEncode(".jpg", gray, out var encoded,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, jpegQuality));
        return encoded;
    }

    private static Point2f[] ToPixelCorners(CropPoint[] normalizedCorners, int width, int height)
        => [.. normalizedCorners.Select(p => new Point2f((float)(p.X * width), (float)(p.Y * height)))];

    private static async Task WriteCropDecision(string imageFile, string workingDirectoryPath, CroppingMode mode,
        CropStrategy finalStrategy, string reason, OpenCvDetectionResult openCv, LlmCropEvidence? llm,
        CancellationToken cancellationToken)
    {
        var artifact = new CropDecisionArtifact
        {
            SourceFile = Path.GetFileName(imageFile),
            Mode = mode,
            FinalStrategy = finalStrategy,
            Reason = reason,
            OpenCv = new OpenCvCropEvidence
            {
                Detected = openCv.Corners is not null,
                Confidence = openCv.Confidence,
                Score = openCv.Score,
                AreaRatio = openCv.AreaRatio,
                EdgeMargin = openCv.EdgeMargin,
                Rectangularity = openCv.Rectangularity,
                AspectPlausibility = openCv.AspectPlausibility,
                Reason = openCv.Reason,
                Corners = openCv.Corners?.Select(p => new CropPoint(p.X, p.Y)).ToArray()
            },
            Llm = llm
        };

        var fileName = Path.GetFileNameWithoutExtension(imageFile) + ".cropDecision.json";
        var path = Path.Combine(workingDirectoryPath, fileName);
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(artifact, s_jsonOptions), cancellationToken);
    }

    private static async Task<string?> WriteRawLlmResponse(string imageFile, string workingDirectoryPath,
        string rawResponse, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return null;
        }

        var fileName = Path.GetFileNameWithoutExtension(imageFile) + ".cropResponse.json";
        await File.WriteAllTextAsync(Path.Combine(workingDirectoryPath, fileName), rawResponse, cancellationToken);
        return fileName;
    }

    private static byte[] ResizeForLlmCrop(byte[] imageData, int maxDimension)
    {
        if (maxDimension <= 0)
        {
            return imageData;
        }

        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        var maxSide = Math.Max(src.Width, src.Height);
        if (maxSide <= maxDimension)
        {
            return imageData;
        }

        var scale = (double)maxDimension / maxSide;
        using var resized = new Mat();
        Cv2.Resize(src, resized, new Size((int)(src.Width * scale), (int)(src.Height * scale)));
        Cv2.ImEncode(".jpg", resized, out var encoded,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, 85));
        return encoded;
    }

    private static string FormatCropStatus(OpenCvDetectionResult openCv, CropStrategy strategy, string reason,
        LlmCropEvidence? llm)
    {
        return strategy switch
        {
            CropStrategy.OpenCv => $"OpenCV {openCv.Confidence} ({openCv.Score:F2}) -> kept",
            CropStrategy.Llm => $"OpenCV {openCv.Confidence} ({openCv.Score:F2}) -> LLM crop",
            _ when llm?.Attempted == true => $"OpenCV {openCv.Confidence} ({openCv.Score:F2}) -> NoCrop ({reason})",
            _ => $"NoCrop ({reason})"
        };
    }

    private static Mat ResizeForProcessing(Mat src)
    {
        const int targetLongSide = 1200;
        var maxSide = Math.Max(src.Width, src.Height);
        if (maxSide <= targetLongSide)
        {
            return src.Clone();
        }

        var scale = (double)targetLongSide / maxSide;
        var resized = new Mat();
        Cv2.Resize(src, resized, new Size((int)(src.Width * scale), (int)(src.Height * scale)));
        return resized;
    }

    private static Point2f[] OrderCorners(Point2f[] corners)
    {
        var centroid = new Point2f(corners.Average(p => p.X), corners.Average(p => p.Y));
        return [.. corners.OrderBy(p => Math.Atan2(p.Y - centroid.Y, p.X - centroid.X))];
    }

    private static Size ComputeDestSize(Point2f[] orderedCorners)
    {
        var (tl, tr, br, bl) = (orderedCorners[0], orderedCorners[1], orderedCorners[2], orderedCorners[3]);
        var maxWidth = Math.Max(Distance(tl, tr), Distance(bl, br));
        var maxHeight = Math.Max(Distance(tl, bl), Distance(tr, br));
        return new Size((int)maxWidth, (int)maxHeight);
    }

    private static double Distance(Point2f a, Point2f b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static void AutoLevels(Mat gray)
    {
        using var hist = new Mat();
        Cv2.CalcHist(new[] { gray }, [0], null, hist, 1, [256], [new[] { 0f, 256f }]);

        var total = gray.Width * gray.Height;
        var skip = (int)(total * 0.005);

        var minVal = 0;
        var maxVal = 255;
        var sum = 0;
        for (var i = 0; i < 256; i++)
        {
            sum += (int)hist.Get<float>(i);
            if (sum > skip)
            {
                minVal = i;
                break;
            }
        }

        sum = 0;
        for (var i = 255; i >= 0; i--)
        {
            sum += (int)hist.Get<float>(i);
            if (sum > skip)
            {
                maxVal = i;
                break;
            }
        }

        if (maxVal > minVal)
        {
            var scale = 255.0 / (maxVal - minVal);
            gray.ConvertTo(gray, MatType.CV_8UC1, scale, -minVal * scale);
        }
    }

    public static void ApplyClahe(Mat gray, double clipLimit = 2.0, int tileGridSize = 8)
    {
        using var clahe = Cv2.CreateCLAHE(clipLimit, new Size(tileGridSize, tileGridSize));
        clahe.Apply(gray, gray);
    }

    public static void ApplyAdaptiveThreshold(Mat gray, int blockSize = 11, double c = 2.0)
    {
        Cv2.AdaptiveThreshold(gray, gray, 255,
            AdaptiveThresholdTypes.GaussianC, ThresholdTypes.Binary,
            blockSize, c);
    }

    public static void ApplyBilateralFilter(Mat src, int d = 9, double sigmaColor = 75, double sigmaSpace = 75)
    {
        using var temp = new Mat();
        Cv2.BilateralFilter(src, temp, d, sigmaColor, sigmaSpace);
        temp.CopyTo(src);
    }

    public static void ApplyAdjustments(Mat gray, double brightness = 0, double contrast = 1.0, double gamma = 1.0)
    {
        if (Math.Abs(gamma - 1.0) > 0.001)
        {
            var lut = new Mat(1, 256, MatType.CV_8UC1);
            var indexer = lut.GetGenericIndexer<byte>();
            for (var i = 0; i < 256; i++)
            {
                indexer[0, i] = (byte)Math.Clamp(255.0 * Math.Pow(i / 255.0, 1.0 / gamma), 0, 255);
            }

            Cv2.LUT(gray, lut, gray);
            lut.Dispose();
        }

        if (Math.Abs(contrast - 1.0) > 0.001 || Math.Abs(brightness) > 0.001)
        {
            gray.ConvertTo(gray, MatType.CV_8UC1, contrast, brightness);
        }
    }
}
