using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paperoni.Contract;
using Paperoni.ImageProcessing;

namespace Paperoni.Tests;

public class LlmCroppingTests
{
    [Fact]
    public async Task ForceLlmCrop_UsesMockedAiAndWritesCropDecisionArtifact()
    {
        var tempBase = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        var albumId = 42;
        var workDir = Path.Combine(tempBase, albumId.ToString());
        Directory.CreateDirectory(workDir);

        var assemblyDir = Path.GetDirectoryName(typeof(LlmCroppingTests).Assembly.Location)!;
        File.Copy(Path.Combine(assemblyDir, "Images", "example-doc.png"),
            Path.Combine(workDir, "1.png"));

        var workingDirectory = new WorkingDirectory { PaperoniWorkingDirectory = tempBase };
        await workingDirectory.WriteData(albumId, new PipelineResult("Test LLM Crop", new Dictionary<string, object>()));

        var fakeChatClient = new FakeChatClient
        {
            Responses =
            [
                """{"crop":[[0.05,0.05],[0.95,0.05],[0.95,0.95],[0.05,0.95]]}"""
            ]
        };

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Cropping:Mode"] = "OpenCvOnly",
                ["Cropping:LlmTimeoutSeconds"] = "30",
                ["Cropping:LlmMaxConcurrency"] = "2"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton(workingDirectory);
        services.AddSingleton<IChatClient>(fakeChatClient);
        services.AddImageProcessing(config);
        services.AddLogging();

        await using var sp = services.BuildServiceProvider();
        var pdfCreator = sp.GetRequiredService<IPdfCreator>();

        var statusMessages = new List<string>();
        await pdfCreator.CreatePdf(albumId, status =>
        {
            statusMessages.Add(status);
            return Task.CompletedTask;
        }, forceLlmCrop: true);

        Assert.Equal(1, fakeChatClient.InvocationCount);
        Assert.Contains(statusMessages, s => s.Contains("LLM crop", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(workDir, "Test LLM Crop.pdf")));

        var decisionPath = Path.Combine(workDir, "1.cropDecision.json");
        Assert.True(File.Exists(decisionPath), $"Expected crop decision artifact at {decisionPath}");

        var jsonOptions = new JsonSerializerOptions(JsonSerializerOptions.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var artifactJson = await File.ReadAllTextAsync(decisionPath);
        var artifact = JsonSerializer.Deserialize<CropDecisionArtifact>(artifactJson, jsonOptions);
        Assert.NotNull(artifact);
        Assert.Contains("\"finalStrategy\": \"Llm\"", artifactJson);
        Assert.Equal(CropStrategy.Llm, artifact.FinalStrategy);
        Assert.True(artifact.Llm?.Succeeded);
        Assert.True(File.Exists(Path.Combine(workDir, "1.cropResponse.json")));
    }
}
