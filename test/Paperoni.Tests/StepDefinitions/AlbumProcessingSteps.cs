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
using Paperoni.ImageProcessing;
using Paperoni.Telegram;
using Paperoni.Telegram.Album;
using Reqnroll;
using UglyToad.PdfPig;
using Xunit.Abstractions;

namespace Paperoni.Tests.StepDefinitions;

[Binding]
public class AlbumProcessingSteps
{
    private readonly ITestOutputHelper _output;
    private string _tempBase = null!;
    private string _outputDir = null!;
    private string _promptFilePath = null!;
    private ServiceProvider _sp = null!;
    private AlbumQueue _queue = null!;
    private FakeTelegramReplier _telegram = null!;
    private CancellationTokenSource? _cts;
    private TracerProvider? _tracerProvider;
    private const int TestMessageId = 42;

    public AlbumProcessingSteps(ITestOutputHelper output)
    {
        _output = output;
    }

    [Given("the album has (.*) photos")]
    public void GivenAlbumHasPhotos(int count)
    {
        var msgDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var assemblyDir = Path.GetDirectoryName(typeof(AlbumProcessingSteps).Assembly.Location)!;

        var extraImages = new[] { "blue.png", "red.png" };
        for (var i = 0; i < count - 1 && i < extraImages.Length; i++)
        {
            File.Copy(Path.Combine(assemblyDir, "Images", extraImages[i]),
                Path.Combine(msgDir, $"{i + 2}.jpg"), overwrite: true);
        }
    }

    [Given("the AI service is unresponsive")]
    public void GivenAiServiceIsUnresponsive()
    {
        var fakeAi = _sp.GetRequiredService<FakeAiService>();
        fakeAi.ShouldThrowOnCreateAiSummary = true;
    }

    [Given("the system is configured for integration testing")]
    public async Task GivenSystemIsConfigured()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _outputDir = Path.Combine(_tempBase, "test-output");
        Directory.CreateDirectory(_tempBase);
        _promptFilePath = Path.Combine(_tempBase, "prompt.md");

        var msgDir = Path.Combine(_tempBase, TestMessageId.ToString());
        Directory.CreateDirectory(msgDir);

        var assemblyDir = Path.GetDirectoryName(typeof(AlbumProcessingSteps).Assembly.Location)!;
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

    [Given("the processing pipeline is built")]
    public void GivenPipelineIsBuilt()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PromptFilePath"] = _promptFilePath,
                ["TestMode"] = "true",
                ["TestModeOutputPath"] = _outputDir,
            })
            .Build();

        _output.WriteLine($"Test output directory: {_tempBase}");
        _output.WriteLine($"Test output: {_outputDir}");

        var services = new ServiceCollection();
        services.AddSingleton(_queue);
        services.AddSingleton<AlbumWorkingDirectory>(_ => new AlbumWorkingDirectory { DownloadBasePath = _tempBase });
        services.AddSingleton<AlbumIdAccessor>();
        services.AddSingleton<ITelegramReplier>(_telegram);
        services.AddSingleton<FakeAiService>();
        services.AddSingleton<IAiService>(sp => sp.GetRequiredService<FakeAiService>());
        services.AddImageProcessing();
        services.AddAlbumProcessor();
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging();

        _sp = services.BuildServiceProvider();
    }

    [When("I enqueue a photo with caption {string}")]
    public void WhenEnqueuePhoto(string caption)
    {
        var photos = new SortedDictionary<int, TelegramPhotoFile>
        {
            { TestMessageId, new TelegramPhotoFile(12345, TestMessageId, "file1", "unique1", caption, DateTime.Now) }
        };
        _queue.Enqueue(new WorkItem(TestMessageId, false));
    }

    private bool _servicesStarted;

    [Given("the pipeline is started")]
    public async Task GivenPipelineStarted()
    {
        if (_servicesStarted)
            return;
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var hostedServices = _sp.GetServices<IHostedService>();
        foreach (var service in hostedServices)
            await service.StartAsync(_cts.Token);
        _servicesStarted = true;
    }

    [Then("the album is processed")]
    public async Task ThenAlbumIsProcessed()
    {
        await GivenPipelineStarted();
        await _telegram.WaitForCompletionAsync(_cts.Token);

        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var content = await File.ReadAllTextAsync(Path.Combine(workDir, "firstAiResponse.md"));
        _output.WriteLine($"AI Summary:\n{content}");
    }

    [Then("the album finishes processing")]
    public async Task ThenAlbumFinishesProcessing()
    {
        await _telegram.WaitForCompletionAsync(_cts.Token);

        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var content = await File.ReadAllTextAsync(Path.Combine(workDir, "firstAiResponse.md"));
        _output.WriteLine($"AI Summary:\n{content}");
        _telegram.Reset();
    }

    [When("I request a retry")]
    public void WhenRequestRetry()
    {
        var queue = _sp.GetRequiredService<AlbumQueue>();
        queue.Enqueue(new WorkItem(TestMessageId, true));
    }

    [Then("the AI summary mentions {string}")]
    public async Task ThenAiSummaryMentions(string expectedText)
    {
        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var content = await File.ReadAllTextAsync(Path.Combine(workDir, "firstAiResponse.md"));
        Assert.Contains(expectedText, content, StringComparison.OrdinalIgnoreCase);
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

    [Then("the PDF has (.*) pages")]
    public void ThenPdfHasPages(int expectedPages)
    {
        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var pdfFiles = Directory.GetFiles(workDir, "*.pdf");
        var pdfPath = Assert.Single(pdfFiles);

        using var pdf = PdfDocument.Open(pdfPath);
        Assert.Equal(expectedPages, pdf.NumberOfPages);
    }

    [Then("the bot reacted with {string}")]
    public void ThenBotReactedWith(string expectedEmoji)
    {
        Assert.Contains(_telegram.Reactions, r => r.Emoji == expectedEmoji);
    }

    [Then("the album processing fails")]
    public async Task ThenAlbumProcessingFails()
    {
        await GivenPipelineStarted();

        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var maxWait = TimeSpan.FromSeconds(30);
        var deadline = DateTime.UtcNow + maxWait;

        while (DateTime.UtcNow < deadline)
        {
            if (_telegram.Calls.Any(c => c.Text.StartsWith("Failed to process")))
                return;
            await Task.Delay(100);
        }

        Assert.Fail("Album did not fail within timeout.");
    }

    [Then("the bot replied with {string}")]
    public void ThenBotRepliedWith(string expectedStartsWith)
    {
        Assert.Contains(_telegram.Calls, c => c.Text.StartsWith(expectedStartsWith));
    }

    [Then("the last bot reply starts with {string}")]
    public void ThenLastBotReplyStartsWith(string expectedStartsWith)
    {
        Assert.StartsWith(expectedStartsWith, _telegram.Calls.Last().Text);
    }

    [Then("the trace log contains expected traces")]
    public void ThenTraceLogContainsExpectedTraces()
    {
        _tracerProvider?.ForceFlush();

        var traceLogPath = Path.Combine(_tempBase, TestMessageId.ToString(), "traces.log");
        Assert.True(File.Exists(traceLogPath), $"Expected trace log at {traceLogPath}");

        var fallbackPath = Path.Combine(_tempBase, "traces.log");
        Assert.False(File.Exists(fallbackPath), $"Unexpected fallback trace log at {fallbackPath} — some spans are missing AlbumId tag");

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
            await ad.DisposeAsync();
    }

    private static string FindSolutionDirectory()
    {
        var dir = new DirectoryInfo(
            Path.GetDirectoryName(typeof(AlbumProcessingSteps).Assembly.Location)!);
        while (dir != null)
        {
            if (dir.GetFiles("Paperoni.slnx").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }
        return Directory.GetCurrentDirectory();
    }
}
