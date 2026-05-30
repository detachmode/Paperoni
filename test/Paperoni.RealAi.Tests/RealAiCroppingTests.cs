using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paperoni.Ai;
using Paperoni.Contract;
using Paperoni.ImageProcessing;

namespace Paperoni.RealAi.Tests;

public class RealAiCroppingTests
{
    [Fact]
    public async Task ForceLlmCrop_CanRunAgainstRealAi_WhenEnabled()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("PAPERONI_RUN_REAL_AI_CROP_TESTS"), "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var tempBase = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var albumId = 42;
        var workDir = Path.Combine(tempBase, albumId.ToString());
        Directory.CreateDirectory(workDir);

        var assemblyDir = Path.GetDirectoryName(typeof(RealAiCroppingTests).Assembly.Location)!;
        File.Copy(Path.Combine(assemblyDir, "Images", "example-doc.png"),
            Path.Combine(workDir, "1.png"));

        var workingDirectory = new WorkingDirectory { PaperoniWorkingDirectory = tempBase };
        await workingDirectory.WriteData(albumId, new PipelineResult("Real AI Crop", new Dictionary<string, object>()));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:Endpoint"] = Environment.GetEnvironmentVariable("AI_ENDPOINT") ?? "http://localhost:2276",
                ["Ai:Model"] = Environment.GetEnvironmentVariable("AI_MODEL") ?? "gemma-4-e4b",
                ["Ai:ApiKey"] = Environment.GetEnvironmentVariable("AI_API_KEY"),
                ["Cropping:Mode"] = "OpenCvOnly",
                ["Cropping:LlmTimeoutSeconds"] = "30",
                ["Cropping:LlmMaxConcurrency"] = "2"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(workingDirectory);
        services.AddAiService(config);
        services.AddImageProcessing(config);
        services.AddLogging();

        await using var sp = services.BuildServiceProvider();
        var pdfCreator = sp.GetRequiredService<IPdfCreator>();

        await pdfCreator.CreatePdf(albumId, forceLlmCrop: true);

        Assert.True(File.Exists(Path.Combine(workDir, "Real AI Crop.pdf")));
        Assert.True(File.Exists(Path.Combine(workDir, "1.cropDecision.json")));
    }
}
