using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Paperoni.ImageProcessing;

internal sealed class LlmCropDetector(IChatClient chatClient, ILogger<LlmCropDetector> logger) : ICropLlmDetector
{
    private const string Prompt = """
        You are an image cropping assistant. Analyze the provided image and find the document (paper, receipt, letter, invoice, etc.) visible in it.

        Return exactly the 4 corners of the document in normalized coordinates (values between 0.0 and 1.0, where [0.0, 0.0] is top-left and [1.0, 1.0] is bottom-right).
        The 4 points can be in any order.

        Respond with a JSON object in this exact format:
        { "crop": [[0.15, 0.25], [0.85, 0.25], [0.85, 0.95], [0.15, 0.95]] }

        You may optionally include adjustment parameters:
        { "crop": [[0.15, 0.25], [0.85, 0.25], [0.85, 0.95], [0.15, 0.95]], "adjustments": { "brightness": -10, "contrast": 1.2, "gamma": 0.8 } }

        If there is no document visible in the image, respond with:
        { "crop": null }

        Return ONLY the JSON object. No markdown fences, no explanation, no other text.
        """;

    public async Task<LlmCropResult> DetectAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        var message = new ChatMessage(ChatRole.User, Prompt);
        message.Contents.Add(new DataContent(imageData, "image/jpeg"));

        try
        {
            var response = await chatClient.GetResponseAsync([message], cancellationToken: cancellationToken);
            var raw = response.Text ?? string.Empty;
            return Parse(raw);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "LLM crop detection failed");
            return new LlmCropResult { Succeeded = false, RawResponse = string.Empty, Error = ex.Message };
        }
    }

    private static LlmCropResult Parse(string raw)
    {
        try
        {
            var json = ExtractJson(raw);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("crop", out var cropElement))
            {
                return new LlmCropResult { Succeeded = false, RawResponse = raw, Error = "Missing crop property" };
            }

            if (cropElement.ValueKind == JsonValueKind.Null)
            {
                return new LlmCropResult { Succeeded = false, RawResponse = raw, Error = "No document" };
            }

            var corners = ParseCorners(cropElement);
            var adjustments = ParseAdjustments(doc.RootElement);
            return new LlmCropResult
            {
                Succeeded = true,
                RawResponse = raw,
                NormalizedCorners = corners,
                Adjustments = adjustments
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return new LlmCropResult { Succeeded = false, RawResponse = raw, Error = ex.Message };
        }
    }

    private static CropPoint[] ParseCorners(JsonElement cropElement)
    {
        if (cropElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("crop must be an array or null");
        }

        var points = new List<CropPoint>();
        foreach (var point in cropElement.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2)
            {
                throw new InvalidOperationException("Each crop point must be [x, y]");
            }

            var x = point[0].GetDouble();
            var y = point[1].GetDouble();
            if (x is < 0 or > 1 || y is < 0 or > 1)
            {
                throw new InvalidOperationException("Crop coordinates must be normalized between 0 and 1");
            }

            points.Add(new CropPoint(x, y));
        }

        if (points.Count != 4)
        {
            throw new InvalidOperationException($"Expected 4 crop points, got {points.Count}");
        }

        return [.. points];
    }

    private static ImageAdjustments? ParseAdjustments(JsonElement root)
    {
        if (!root.TryGetProperty("adjustments", out var adj) || adj.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var brightness = adj.TryGetProperty("brightness", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetDouble() : 0;
        var contrast = adj.TryGetProperty("contrast", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 1.0;
        var gamma = adj.TryGetProperty("gamma", out var g) && g.ValueKind == JsonValueKind.Number ? g.GetDouble() : 1.0;
        return new ImageAdjustments(
            Math.Clamp(brightness, -50, 50),
            Math.Clamp(contrast, 0.5, 2.0),
            Math.Clamp(gamma, 0.3, 3.0));
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end < start)
        {
            throw new JsonException("No JSON object found in LLM crop response");
        }

        return text[start..(end + 1)];
    }
}
