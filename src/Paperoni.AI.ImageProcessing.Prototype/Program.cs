using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;
using OpenCvSharp;
using Paperoni.ImageProcessing;

if (args.Length < 2 || args[0] != "--input")
{
    Console.Error.WriteLine(
        "Usage: Paperoni.AI.ImageProcessing.Prototype --input <file1.jpg> [file2.jpg ...] [--max-dimension <px>] [--auto-levels] [--auto-levels-only] [--clahe-clip <value>]");
    return 1;
}

var inputFiles = args[1..]
    .TakeWhile(a => a is not ("--max-dimension" or "--auto-levels" or "--auto-levels-only" or "--clahe-clip"))
    .ToArray();
if (inputFiles.Length == 0)
{
    Console.Error.WriteLine("No input files specified after --input");
    return 1;
}

var maxDimIdx = Array.IndexOf(args, "--max-dimension");
var maxDimension = 0;
if (maxDimIdx >= 0 && maxDimIdx + 1 < args.Length && int.TryParse(args[maxDimIdx + 1], out var dim))
{
    maxDimension = dim;
}

var autoLevels = args.Contains("--auto-levels");
var autoLevelsOnly = args.Contains("--auto-levels-only");

var claheClip = 2.0;

var claheClipIdx = Array.IndexOf(args, "--clahe-clip");
if (claheClipIdx >= 0 && claheClipIdx + 1 < args.Length && double.TryParse(args[claheClipIdx + 1], out var clip))
{
    claheClip = clip;
}

if (autoLevelsOnly)
{
    Console.WriteLine($"Auto-levels only: CLAHE (clip={claheClip})");
    Console.WriteLine();

    foreach (var inputFile in inputFiles)
    {
        var fullPath = Path.GetFullPath(inputFile);
        if (!File.Exists(fullPath))
        {
            Console.Error.WriteLine($"File not found: {fullPath}");
            return 1;
        }

        var sw = Stopwatch.StartNew();
        var imageData = await File.ReadAllBytesAsync(fullPath);

        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        Console.Write($"{Path.GetFileName(fullPath)} ({src.Width}x{src.Height}) ... ");

        var outputBytes = ToGrayscaleJpeg(imageData, claheClipLimit: claheClip);
        sw.Stop();

        var basePath = Path.ChangeExtension(fullPath, null);
        var outputPath = $"{basePath}_clahe.jpg";
        await File.WriteAllBytesAsync(outputPath, outputBytes);
        Console.WriteLine($"-> {Path.GetFileName(outputPath)} ({sw.Elapsed.TotalSeconds:F1}s)");
    }

    Console.WriteLine("Done.");
    return 0;
}

var config = LoadConfiguration();

var aiSection = config.GetSection("Ai");
var endpoint = aiSection["Endpoint"]
               ?? throw new InvalidOperationException("Ai:Endpoint is required in appsettings.json");
var model = aiSection["Model"]
            ?? throw new InvalidOperationException("Ai:Model is required in appsettings.json");
var apiKey = aiSection["ApiKey"];

Console.WriteLine($"Endpoint: {endpoint}");
Console.WriteLine($"Model: {model}");
Console.WriteLine($"ApiKey: {(string.IsNullOrEmpty(apiKey) ? "not set" : "***")}");
if (maxDimension > 0)
{
    Console.WriteLine($"Max dimension: {maxDimension}px");
}

if (autoLevels)
{
    Console.WriteLine($"Auto-levels: CLAHE (clip={claheClip})");
}

Console.WriteLine();

var promptPath = Path.Combine(AppContext.BaseDirectory, "PromptCrop.md");
if (!File.Exists(promptPath))
{
    Console.Error.WriteLine($"Prompt file not found: {promptPath}");
    return 1;
}

var prompt = File.ReadAllText(promptPath);

var endpointUri = new Uri(endpoint);
var isLocal = endpointUri.IsLoopback ||
              string.Equals(endpointUri.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(endpointUri.Host, "host.docker.internal", StringComparison.OrdinalIgnoreCase);

if (isLocal && string.IsNullOrEmpty(apiKey))
{
    apiKey = "api-key";
}

if (string.IsNullOrEmpty(apiKey))
{
    throw new InvalidOperationException("Ai:ApiKey is required for remote endpoints");
}

var openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey),
    new OpenAIClientOptions { Endpoint = endpointUri });
var chatClient = openAiClient.GetChatClient(model).AsIChatClient();

var modelSuffix = SanitizeForFilename(model);

var claheClipValue = autoLevels ? claheClip : (double?)null;

foreach (var inputFile in inputFiles)
{
    var fullPath = Path.GetFullPath(inputFile);
    if (!File.Exists(fullPath))
    {
        Console.Error.WriteLine($"File not found: {fullPath}");
        return 1;
    }

    Console.Write($"Processing: {Path.GetFileName(fullPath)} ... ");

    var imageData = await File.ReadAllBytesAsync(fullPath);

    using var originalSrc = Cv2.ImDecode(imageData, ImreadModes.Color);
    var origW = originalSrc.Width;
    var origH = originalSrc.Height;

    byte[] aiImageData;
    if (maxDimension > 0 && Math.Max(origW, origH) > maxDimension)
    {
        var resized = ResizeImage(originalSrc, maxDimension);
        Cv2.ImEncode(".jpg", resized, out var resizedBytes,
            new ImageEncodingParam(ImwriteFlags.JpegQuality, 85));
        resized.Dispose();
        aiImageData = resizedBytes;
        using var check = Cv2.ImDecode(aiImageData, ImreadModes.Color);
        Console.Write($"{origW}x{origH} -> {check.Width}x{check.Height} ... ");
    }
    else
    {
        aiImageData = imageData;
        Console.Write($"{origW}x{origH} ... ");
    }

    var message = new ChatMessage(ChatRole.User, prompt);
    message.Contents.Add(new DataContent(aiImageData, "image/jpeg"));

    var aiSw = Stopwatch.StartNew();
    var response = await chatClient.GetResponseAsync(message);
    aiSw.Stop();

    var responseText = response.Text;

    var basePath = Path.ChangeExtension(fullPath, null);

    await File.WriteAllTextAsync(basePath + "_response.json", responseText);

    var json = ExtractJson(responseText);
    using var doc = JsonDocument.Parse(json);

    if (!doc.RootElement.TryGetProperty("crop", out var cropElement))
    {
        throw new InvalidOperationException("AI response missing 'crop' field");
    }

    byte[] outputBytes;

    if (cropElement.ValueKind == JsonValueKind.Null)
    {
        Console.Write("no document, grayscale only");
        if (autoLevels)
        {
            Console.Write(" + CLAHE");
        }

        Console.Write(" ... ");
        outputBytes = ToGrayscaleJpeg(imageData, claheClipLimit: claheClipValue);
    }
    else
    {
        var corners = ParseCorners(cropElement);

        using var src = Cv2.ImDecode(imageData, ImreadModes.Color);
        var pixelCorners = corners
            .Select(p => new Point2f(p.X * src.Width, p.Y * src.Height))
            .ToArray();

        outputBytes = PdfCreator.ApplyCrop(imageData, pixelCorners, claheClipLimit: claheClipValue);
        Console.Write("cropped");
        if (autoLevels)
        {
            Console.Write(" + CLAHE");
        }

        Console.Write(" ... ");
    }

    var outputPath = $"{basePath}_ai_{modelSuffix}.jpg";
    await File.WriteAllBytesAsync(outputPath, outputBytes);
    Console.WriteLine($"-> {Path.GetFileName(outputPath)} (AI: {aiSw.Elapsed.TotalSeconds:F1}s)");
}

Console.WriteLine("Done.");
return 0;

static IConfiguration LoadConfiguration()
{
    var builder = new ConfigurationBuilder();

    var configPath = FindAppSettings();

    if (configPath == null)
    {
        throw new InvalidOperationException(
            "appsettings.json not found. Searched ancestor directories and src/Paperoni/ relative to them.");
    }

    Console.WriteLine($"Config: {configPath}");
    builder.AddJsonFile(configPath, optional: false);

    var devPath = Path.Combine(Path.GetDirectoryName(configPath)!, "appsettings.Development.json");
    if (File.Exists(devPath))
    {
        builder.AddJsonFile(devPath, optional: true);
    }

    builder.AddUserSecrets(typeof(Program).Assembly);

    return builder.Build();
}

static string? FindAppSettings()
{
    var searchDir = Directory.GetCurrentDirectory();
    while (searchDir != null)
    {
        var direct = Path.Combine(searchDir, "appsettings.json");
        if (File.Exists(direct))
        {
            return direct;
        }

        var paperoniPath = Path.Combine(searchDir, "src", "Paperoni", "appsettings.json");
        if (File.Exists(paperoniPath))
        {
            return paperoniPath;
        }

        searchDir = Path.GetDirectoryName(searchDir);
    }

    return null;
}

static Mat ResizeImage(Mat src, int maxDimension)
{
    var maxSide = Math.Max(src.Width, src.Height);
    var scale = (double)maxDimension / maxSide;
    var resized = new Mat();
    Cv2.Resize(src, resized, new Size((int)(src.Width * scale), (int)(src.Height * scale)));
    return resized;
}

static string SanitizeForFilename(string name)
{
    var invalid = Path.GetInvalidFileNameChars();
    return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
}

static string ExtractJson(string text)
{
    text = text.Trim();

    if (text.StartsWith("```"))
    {
        var newlineIdx = text.IndexOf('\n');
        if (newlineIdx >= 0)
        {
            text = text[(newlineIdx + 1)..];
            if (text.EndsWith("```"))
            {
                text = text[..^3].TrimEnd();
            }
        }
    }

    text = text.Trim();

    var start = text.IndexOf('{');
    var end = text.LastIndexOf('}');
    if (start < 0 || end < 0)
    {
        throw new InvalidOperationException(
            $"AI response does not contain a JSON object: {text[..Math.Min(200, text.Length)]}");
    }

    return text[start..(end + 1)];
}

static Point2f[] ParseCorners(JsonElement cropElement)
{
    if (cropElement.ValueKind != JsonValueKind.Array)
    {
        throw new InvalidOperationException(
            $"'crop' must be an array of 4 points, got: {cropElement.ValueKind}");
    }

    var points = new List<Point2f>();
    foreach (var point in cropElement.EnumerateArray())
    {
        if (point.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Each crop point must be a [x, y] array");
        }

        var coords = point.EnumerateArray().Select(c => c.GetSingle()).ToArray();
        if (coords.Length != 2)
        {
            throw new InvalidOperationException(
                "Each crop point must have exactly 2 coordinates [x, y]");
        }

        if (coords[0] < 0 || coords[0] > 1 || coords[1] < 0 || coords[1] > 1)
        {
            throw new InvalidOperationException(
                $"Coordinates must be in [0,1] range, got [{coords[0]}, {coords[1]}]");
        }

        points.Add(new Point2f(coords[0], coords[1]));
    }

    if (points.Count != 4)
    {
        throw new InvalidOperationException(
            $"Expected exactly 4 crop points, got {points.Count}");
    }

    return points.ToArray();
}

static byte[] ToGrayscaleJpeg(byte[] imageData, int quality = 85, double? claheClipLimit = null)
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

    if (claheClipLimit.HasValue)
    {
        PdfCreator.ApplyClahe(gray, claheClipLimit.Value);
    }

    Cv2.ImEncode(".jpg", gray, out var encoded,
        new ImageEncodingParam(ImwriteFlags.JpegQuality, quality));
    return encoded;
}
