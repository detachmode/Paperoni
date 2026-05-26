using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Paperoni.Contract;
using static Paperoni.Contract.ActivityExtensions;

namespace Paperoni.ImageProcessing;

public record AutoCorrectImageResult(
    byte[] ImprovedImage,
    string OriginalImagePath
);

internal sealed class PdfCreator(
    ILogger<PdfCreator> logger,
    WorkingDirectory workingDirectory) : IPdfCreator
{
    private static readonly ImageProcessingOptions s_defaultOptions = new();

    public async Task CreatePdf(int messageId, CancellationToken stoppingToken)
    {
        await Tracer.TraceAsync<PdfCreator>(async scope =>
        {
            var sw = Stopwatch.StartNew();

            var downloadPath = workingDirectory.RequireWorkingDirectory(messageId);
            var aiResult = await workingDirectory.GetData<PipelineResult>(messageId, stoppingToken);
            ArgumentNullException.ThrowIfNull(aiResult);

            var files = Directory.GetFiles(downloadPath);
            var originalImages = files.Where(FileHelpers.IsImageFile).OrderBy(Path.GetFileName).ToList();
            scope.SetTag("imageCount", originalImages.Count);
            scope.SetTag("title", aiResult.Filename);

            var processedData = await AutoCorrect(originalImages, stoppingToken);

            var pdfBytes = PdfMerger.MergeToPdf(processedData.Select(i => i.ImprovedImage));
            var pdfPath = Path.Combine(downloadPath, $"{aiResult.Filename}.pdf");

            await File.WriteAllBytesAsync(pdfPath, pdfBytes, stoppingToken);
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

    public async Task<List<AutoCorrectImageResult>> AutoCorrect(List<string> originalImages,
        CancellationToken stoppingToken)
    {
        var result = new List<AutoCorrectImageResult>();
        foreach (var imageFile in originalImages)
        {
            await Tracer.TraceAsync<PdfCreator>(async scope =>
            {
                scope.SetTag("file", Path.GetFileName(imageFile));

                var imageData = await File.ReadAllBytesAsync(imageFile, stoppingToken);
                var processed = await AutoCorrect(imageData, stoppingToken);

                scope.SetTag("documentDetected", processed.DocumentDetected);
                scope.SetTag("processingTimeMs", processed.ProcessingTime.TotalMilliseconds);

                logger.ImageProcessed(Path.GetFileName(imageFile), processed.DocumentDetected,
                    processed.ProcessingTime.TotalMilliseconds);

                result.Add(new AutoCorrectImageResult(processed.ProcessedImage, imageFile));
            });
        }

        return result;
    }

    public static async Task<ProcessedImageResult> AutoCorrect(byte[] imageData, ImageProcessingOptions options,
        CancellationToken ct = default)
        => await Task.Run(() => Process(imageData, options), ct);

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

    private static ProcessedImageResult Process(byte[] imageData, ImageProcessingOptions options)
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
            OriginalHeight = originalHeight
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
