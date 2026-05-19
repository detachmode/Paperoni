using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Paperoni.Ai;
using Paperoni.AlbumProcessing;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using Paperoni.ImageProcessing;
using Paperoni.Telegram;
using Paperoni.Telegram.Album;
using Reqnroll;
using Xunit.Abstractions;

namespace Paperoni.RealAi.Tests.StepDefinitions;

[Binding]
public class AlbumProcessingSmokeSteps
{
    private const int TestMessageId = 42;
    private readonly ITestOutputHelper _output;
    private CancellationTokenSource? _cts;
    private string _outputDir = null!;
    private string _promptFilePath = null!;
    private AlbumQueue _queue = null!;

    private bool _servicesStarted;
    private ServiceProvider _sp = null!;
    private FakeTelegramReplier _telegram = null!;
    private string _tempBase = null!;
    private TracerProvider? _tracerProvider;

    public AlbumProcessingSmokeSteps(ITestOutputHelper output)
    {
        _output = output;
    }

    [Given("the system is configured for real AI integration testing")]
    public async Task GivenSystemIsConfigured()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _outputDir = Path.Combine(_tempBase, "test-output");
        Directory.CreateDirectory(_tempBase);
        _promptFilePath = Path.Combine(_tempBase, "prompt.md");

        var msgDir = Path.Combine(_tempBase, TestMessageId.ToString());
        Directory.CreateDirectory(msgDir);

        var assemblyDir = Path.GetDirectoryName(typeof(AlbumProcessingSmokeSteps).Assembly.Location)!;
        File.Copy(Path.Combine(assemblyDir, "Images", "example-doc.png"),
            Path.Combine(msgDir, "1.jpg"), overwrite: true);

        var meta = new MetaData
        {
            Date = DateTime.Now,
            Caption = ["Test document"],
            MessageId = TestMessageId,
            ChatId = 12345,
            ReplyMessageId = 100,
            AlbumMessageIds = [TestMessageId]
        };
        await File.WriteAllTextAsync(
            Path.Combine(msgDir, "MetaData.json"),
            JsonSerializer.Serialize(meta));

        _telegram = new FakeTelegramReplier();
        _queue = new AlbumQueue(NullLogger<AlbumQueue>.Instance);

        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("Paperoni")
            .AddProcessor(new BatchActivityExportProcessor(
                new TraceLogExporter(
                    new AlbumWorkingDirectory { DownloadBasePath = _tempBase },
                    _tempBase),
                maxQueueSize: 2048,
                scheduledDelayMilliseconds: 100))
            .Build();
    }

    [Given("the prompt template is:")]
    public async Task GivenPromptTemplate(string prompt)
    {
        await File.WriteAllTextAsync(_promptFilePath, prompt);
    }

    [Given("the real AI processing pipeline is built")]
    public void GivenPipelineIsBuilt()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:PromptFilePath"] = _promptFilePath,
                ["AlbumProcessing:TestMode"] = "true",
                ["AlbumProcessing:TestModeOutputPath"] = _outputDir,
                ["AlbumProcessing:MarkdownOutputPath"] = _outputDir,
                ["AlbumProcessing:PdfOutputPath"] = _outputDir,
            })
            .Build();

        _output.WriteLine($"Test output directory: {_tempBase}");
        _output.WriteLine($"Test output: {_outputDir}");

        var services = new ServiceCollection();
        services.AddSingleton(_queue);
        services.AddSingleton<AlbumWorkingDirectory>(_ => new AlbumWorkingDirectory { DownloadBasePath = _tempBase });
        services.AddSingleton<AlbumIdAccessor>();
        services.AddSingleton<ITelegramReplier>(_telegram);
        services.AddAiService(config);
        services.AddImageProcessing();
        services.AddAlbumProcessor(config);
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        _sp = services.BuildServiceProvider();
    }

    [When("I enqueue a photo with caption {string}")]
    public void WhenEnqueuePhoto(string caption)
    {
        _queue.Enqueue(new WorkItem(TestMessageId, false));
    }

    [Then("the album is processed with real AI")]
    public async Task ThenAlbumIsProcessed()
    {
        if (!_servicesStarted)
        {
            _cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            var hostedServices = _sp.GetServices<IHostedService>();
            foreach (var service in hostedServices)
            {
                await service.StartAsync(_cts.Token);
            }

            _servicesStarted = true;
        }

        await _telegram.WaitForCompletionAsync(_cts!.Token);

        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var content = await File.ReadAllTextAsync(Path.Combine(workDir, "firstAiResponse.md"));
        _output.WriteLine($"AI Summary:\n{content}");
    }

    [Then("a PDF is created")]
    public void ThenPdfCreated()
    {
        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var pdfFiles = Directory.GetFiles(workDir, "*.pdf");
        Assert.NotEmpty(pdfFiles);
    }

    [Then("the summary is published to Obsidian")]
    public void ThenSummaryPublishedToObsidian()
    {
        var markdownFiles = Directory.GetFiles(_outputDir, "*.md");
        Assert.NotEmpty(markdownFiles);
    }

    [Then("the PDF is published to the output directory")]
    public void ThenPdfPublishedToOutputDirectory()
    {
        var pdfFiles = Directory.GetFiles(_outputDir, "*.pdf");
        Assert.NotEmpty(pdfFiles);
    }

    [Then("the trace log contains expected traces")]
    public void ThenTraceLogContainsExpectedTraces()
    {
        _tracerProvider?.ForceFlush();

        var traceLogPath = Path.Combine(_tempBase, TestMessageId.ToString(), "traces.log");
        Assert.True(File.Exists(traceLogPath), $"Expected trace log at {traceLogPath}");

        var fallbackPath = Path.Combine(_tempBase, "traces.log");
        Assert.False(File.Exists(fallbackPath),
            $"Unexpected fallback trace log at {fallbackPath} — some spans are missing AlbumId tag");

        var lines = File.ReadAllLines(traceLogPath);
        var joined = string.Join("\n", lines);
        Assert.Contains("AlbumProcessor.ExecuteAsync", joined);
        Assert.Contains("AiService.CreateAiSummary", joined);
        Assert.Contains("PdfCreator.CreatePdf", joined);
        Assert.Contains("FilePublisher.PublishFileAsync", joined);
    }

    [AfterScenario]
    public async Task Cleanup()
    {
        _cts?.Cancel();
        _tracerProvider?.Dispose();
        if (_sp is IAsyncDisposable ad)
        {
            await ad.DisposeAsync();
        }
    }
}
