using System.Diagnostics;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using Paperoni.Contract;
using static Paperoni.Contract.Diagnostics;

namespace Paperoni.ImageProcessing;

public record AutoCorrectImageResult(
    byte[] ImprovedImage,
    string OriginalImagePath
    );

internal sealed class PdfCreator(ILogger<PdfCreator> logger, AlbumWorkingDirectory workingDirectory, AlbumIdAccessor albumIdAccessor) : IPdfCreator
{
    private static readonly ImageProcessingOptions s_defaultOptions = new();

    public static Task<ProcessedImageResult> AutoCorrect(byte[] imageData, CancellationToken ct = default)
        => AutoCorrect(imageData, s_defaultOptions, ct);

    public async Task<List<AutoCorrectImageResult>> AutoCorrect(List<string> originalImages, CancellationToken stoppingToken)
    {
        var result = new List<AutoCorrectImageResult>();
        foreach (var imageFile in originalImages)
        {
            using var activity = Tracer.StartActivity<PdfCreator>();
            activity?.SetTag("AlbumId", albumIdAccessor.Id);
            activity?.SetTag("file", Path.GetFileName(imageFile));

            var imageData = await File.ReadAllBytesAsync(imageFile, stoppingToken);
            var processed = await AutoCorrect(imageData, stoppingToken);

            activity?.SetTag("documentDetected", processed.DocumentDetected);
            activity?.SetTag("processingTimeMs", processed.ProcessingTime.TotalMilliseconds);
            activity?.SetStatus(ActivityStatusCode.Ok);

            logger.ImageProcessed(Path.GetFileName(imageFile), processed.DocumentDetected,
                processed.ProcessingTime.TotalMilliseconds);

             result.Add(new AutoCorrectImageResult(processed.ProcessedImage, imageFile));
        }
        return result;
    }

    public async Task CreatePdf(int messageId, CancellationToken stoppingToken)
    {
        using var activity = Tracer.StartActivity<PdfCreator>();
        activity?.SetTag("AlbumId", messageId);
        var sw = Stopwatch.StartNew();

        var downloadPath = workingDirectory.GetDownloadPath(messageId);
        var aiResult = await workingDirectory.GetData<AiResult>(messageId, stoppingToken);
        ArgumentNullException.ThrowIfNull(aiResult);

        var files = Directory.GetFiles(downloadPath);
        var originalImages = files.Where(FileHelpers.IsImageFile).OrderBy(Path.GetFileName).ToList();
        activity?.SetTag("imageCount", originalImages.Count);
        activity?.SetTag("title", aiResult.Title);

        var processedData = await AutoCorrect(originalImages, stoppingToken);

        var pdfBytes = PdfMerger.MergeToPdf(processedData.Select(i => i.ImprovedImage));
        var pdfPath = Path.Combine(downloadPath, $"{aiResult.Title}.pdf");

        await File.WriteAllBytesAsync(pdfPath, pdfBytes, stoppingToken);
        sw.Stop();

        activity?.SetTag("file", Path.GetFileName(pdfPath));
        activity?.SetTag("sizeKb", pdfBytes.Length / 1024);
        activity?.SetTag("pages", originalImages.Count);
        activity?.SetTag("latencySec", sw.Elapsed.TotalSeconds);
        activity?.SetStatus(ActivityStatusCode.Ok);

        logger.PdfCreated(messageId, Path.GetFileName(pdfPath), pdfBytes.Length / 1024,
            originalImages.Count, sw.Elapsed.TotalSeconds);
    }

    public static async Task<ProcessedImageResult> AutoCorrect(byte[] imageData, ImageProcessingOptions options, CancellationToken ct = default)
        => await Task.Run(() => Process(imageData, options), ct);

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
                        new Point2f(0, 0),
                        new Point2f(destSize.Width - 1, 0),
                        new Point2f(destSize.Width - 1, destSize.Height - 1),
                        new Point2f(0, destSize.Height - 1)
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

        Cv2.ImEncode(".jpg", output, out var encoded, new ImageEncodingParam(ImwriteFlags.JpegQuality, options.JpegQuality));

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

    private static void AutoLevels(Mat gray)
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
            if (sum > skip) { minVal = i; break; }
        }

        sum = 0;
        for (var i = 255; i >= 0; i--)
        {
            sum += (int)hist.Get<float>(i);
            if (sum > skip) { maxVal = i; break; }
        }

        if (maxVal > minVal)
        {
            var scale = 255.0 / (maxVal - minVal);
            gray.ConvertTo(gray, MatType.CV_8UC1, scale, -minVal * scale);
        }
    }
}
