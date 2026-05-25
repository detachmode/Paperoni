using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.AI;
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
using Serilog;
using UglyToad.PdfPig;
using Xunit.Abstractions;

namespace Paperoni.Tests.StepDefinitions;

[Binding]
public class AlbumProcessingSteps
{
    private const int TestMessageId = 42;
    private const int UnknownMessageId = 999;
    private readonly ITestOutputHelper _output;
    private CancellationTokenSource? _cts;
    private SpyFilePublisher _markdownSpy = null!;
    private string _outputDir = null!;
    private SpyFilePublisher _pdfSpy = null!;
    private string _scriptFilePath = null!;
    private AlbumQueue _queue = null!;
    private FakeChatClient _fakeChatClient = null!;

    private bool _servicesStarted;
    private ServiceProvider _sp = null!;
    private FakeTelegramReplier _telegram = null!;
    private string _tempBase = null!;
    private TracerProvider? _tracerProvider;
    private int _activeMessageId = TestMessageId;

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

    [Given("the pipeline service is unresponsive")]
    public void GivenPipelineServiceIsUnresponsive()
    {
        _fakeChatClient.ShouldThrow = true;
    }

    [Given("the pipeline script has a compile error")]
    public async Task GivenPipelineScriptHasCompileError()
    {
        await File.WriteAllTextAsync(_scriptFilePath, "this is not valid C#;");
    }

    [Given("the LLM will return invalid JSON on the first attempt")]
    public void GivenLlmReturnsInvalidJsonOnFirstAttempt()
    {
        _fakeChatClient.Responses =
        [
            """{"Title": 123, "Summary": null, "MarkdownBody": true}""",
            """{"Title":"Lorem Ipsum","Summary":"Fake summary for testing","MarkdownBody":"Fake AI summary for testing."}"""
        ];
    }

    [Given("the LLM will return empty title on the first attempt")]
    public void GivenLlmReturnsEmptyTitleOnFirstAttempt()
    {
        _fakeChatClient.Responses =
        [
            """{"Title":"","Summary":"Fake summary","MarkdownBody":"Fake body."}""",
            """{"Title":"Lorem Ipsum","Summary":"Fake summary for testing","MarkdownBody":"Fake AI summary for testing."}"""
        ];
    }

    [Given("the system is configured for integration testing")]
    public async Task GivenSystemIsConfigured()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _outputDir = Path.Combine(_tempBase, "test-output");
        Directory.CreateDirectory(_tempBase);
        _scriptFilePath = Path.Combine(_tempBase, "pipeline.csx");

        var msgDir = Path.Combine(_tempBase, TestMessageId.ToString());
        Directory.CreateDirectory(msgDir);

        var assemblyDir = Path.GetDirectoryName(typeof(AlbumProcessingSteps).Assembly.Location)!;

        // Copy the test pipeline script
        var testScriptPath = Path.Combine(assemblyDir, "TestPipeline.csx");
        File.Copy(testScriptPath, _scriptFilePath, overwrite: true);

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
            .AddProcessor(new SimpleActivityExportProcessor(
                new TraceLogExporter(
                    new WorkingDirectory { PaperoniWorkingDirectory = _tempBase },
                    _tempBase)))
            .Build();
    }

    [Given("the pipeline script is:")]
    public async Task GivenPipelineScript(string script)
    {
        await File.WriteAllTextAsync(_scriptFilePath, script);
    }

    [Given("the processing pipeline is built")]
    public void GivenPipelineIsBuilt()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AlbumProcessing:ScriptFilePath"] = _scriptFilePath,
                ["AlbumProcessing:TestMode"] = "true",
                ["AlbumProcessing:TestModeOutputPath"] = _outputDir,
                ["AlbumProcessing:MarkdownOutputPath"] = _outputDir,
                ["AlbumProcessing:PdfOutputPath"] = _outputDir,
                ["Ai:Endpoint"] = "http://localhost:2276",
                ["Ai:Model"] = "fake-model",
            })
            .Build();

        _output.WriteLine($"Test output directory: {_tempBase}");
        _output.WriteLine($"Test output: {_outputDir}");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(_tempBase, "paperoni.log"),
                outputTemplate:
                "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{Level:u3}] [AlbumId={AlbumId}] {Message:lj}{NewLine}{Exception}",
                shared: true)
            .CreateLogger();

        _fakeChatClient = new FakeChatClient();

        var services = new ServiceCollection();
        services.AddSingleton(_queue);
        services.AddSingleton<WorkingDirectory>(_ => new WorkingDirectory { PaperoniWorkingDirectory = _tempBase });
        services.AddSingleton<AlbumIdAccessor>();
        services.AddSingleton<ITelegramReplier>(_telegram);
        services.AddAiService(config);
        services.AddSingleton<IChatClient>(_fakeChatClient);
        services.AddImageProcessing();
        services.AddAlbumProcessor(config);
        services.AddDiagnostics(config);

        services.AddKeyedSingleton<IFilePublisher>(PublisherTarget.Markdown, (_, _) =>
        {
            var real = new FilePublisher(_outputDir, NullLogger<FilePublisher>.Instance);
            _markdownSpy = new SpyFilePublisher(real);
            return _markdownSpy;
        });
        services.AddKeyedSingleton<IFilePublisher>(PublisherTarget.Pdf, (_, _) =>
        {
            var real = new FilePublisher(_outputDir, NullLogger<FilePublisher>.Instance);
            _pdfSpy = new SpyFilePublisher(real);
            return _pdfSpy;
        });
        services.AddSingleton<IConfiguration>(config);
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            builder.AddSerilog(Log.Logger, true);
        });

        _sp = services.BuildServiceProvider();
    }

    [When("I enqueue the message")]
    public void WhenEnqueueTheMessage()
    {
        _activeMessageId = TestMessageId;
        _queue.Enqueue(new WorkItem(TestMessageId, false));
    }

    [Given("the pipeline is started")]
    public async Task GivenPipelineStarted()
    {
        if (_servicesStarted)
        {
            return;
        }

        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var hostedServices = _sp.GetServices<IHostedService>();
        foreach (var service in hostedServices)
        {
            await service.StartAsync(_cts.Token);
        }

        _servicesStarted = true;
    }

    [Then("the album is processed")]
    public async Task ThenAlbumIsProcessed()
    {
        await GivenPipelineStarted();
        await _telegram.WaitForCompletionAsync(_cts!.Token);

        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var content = await File.ReadAllTextAsync(Path.Combine(workDir, "firstAiResponse.json"));
        _output.WriteLine($"AI Response:\n{content}");
    }

    [Then("the album finishes processing")]
    public async Task ThenAlbumFinishesProcessing()
    {
        await _telegram.WaitForCompletionAsync(_cts!.Token);

        _tracerProvider?.ForceFlush();

        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var content = await File.ReadAllTextAsync(Path.Combine(workDir, "firstAiResponse.json"));
        _output.WriteLine($"AI Summary:\n{content}");
        _telegram.Reset();
    }

    [When("I request diagnostics for the message")]
    public async Task WhenIRequestDiagnostics()
    {
        await _telegram.ShowDiagnostic(TestMessageId);
    }

    [When("I request a retry")]
    public void WhenRequestRetry()
    {
        _activeMessageId = TestMessageId;
        var queue = _sp.GetRequiredService<AlbumQueue>();
        queue.Enqueue(new WorkItem(TestMessageId, true));
    }

    [When("I request a retry for an unknown album id")]
    public async Task WhenIRequestARetryForAnUnknownAlbumId()
    {
        await GivenPipelineStarted();
        _activeMessageId = UnknownMessageId;
        var queue = _sp.GetRequiredService<AlbumQueue>();
        queue.Enqueue(new WorkItem(UnknownMessageId, true));
    }

    [Then("the PipelineResult is persisted with filename {string}")]
    public async Task ThenPipelineResultIsPersisted(string expectedFilename)
    {
        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var resultPath = Path.Combine(workDir, "PipelineResult.json");
        Assert.True(File.Exists(resultPath), $"Expected PipelineResult.json at {resultPath}");

        var json = await File.ReadAllTextAsync(resultPath);
        var result = JsonSerializer.Deserialize<PipelineResult>(json);
        Assert.NotNull(result);
        Assert.Matches(expectedFilename, result.Filename);
    }

    [Then("the formatted markdown is published to Obsidian")]
    public void ThenFormattedMarkdownPublished()
    {
        var markdownFiles = Directory.GetFiles(_outputDir, "*.md");
        Assert.NotEmpty(markdownFiles);
    }

    [Then("a PDF is created with filename {string}")]
    public void ThenPdfCreatedWithFilename(string expectedFilename)
    {
        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var pdfPath = Path.Combine(workDir, $"{expectedFilename}.pdf");
        Assert.True(File.Exists(pdfPath), $"Expected PDF at {pdfPath}");
    }

    [Then("a PDF is created")]
    public void ThenPdfCreated()
    {
        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var pdfFiles = Directory.GetFiles(workDir, "*.pdf");
        Assert.NotEmpty(pdfFiles);
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

    [Then("the diagnostic was shown for the album")]
    public void ThenDiagnosticWasShownForTheAlbum()
    {
        Assert.Contains(TestMessageId, _telegram.DiagnosticAlbumIds);
    }

    [Then("the dashboard was deleted")]
    public void ThenDashboardWasDeleted()
    {
        Assert.Equal(1, _telegram.DeleteDashboardCount);
    }

    [Then("the old published files were cleaned before re-publishing")]
    public void ThenOldPublishedFilesWereCleaned()
    {
        Assert.True(_markdownSpy.DeletePreviousCalled, "Expected Markdown publisher to clean old file");
        Assert.True(_pdfSpy.DeletePreviousCalled, "Expected PDF publisher to clean old file");
        Assert.Equal(".md", _markdownSpy.LastDeletedExtension);
        Assert.Equal(".pdf", _pdfSpy.LastDeletedExtension);
        Assert.Equal("Lorem Ipsum", _markdownSpy.LastDeletedFilename);
        Assert.Equal("Lorem Ipsum", _pdfSpy.LastDeletedFilename);
    }

    [Then("the old trace log was cleaned before re-processing")]
    public void ThenOldTraceLogWasCleanedBeforeReprocessing()
    {
        _tracerProvider?.ForceFlush();

        var traceLogPath = Path.Combine(_tempBase, TestMessageId.ToString(), "traces.log");
        Assert.True(File.Exists(traceLogPath));

        var lines = File.ReadAllLines(traceLogPath);
        var spanCount = lines.Count(l => l.Contains("AlbumProcessor.ExecuteAsync"));
        Assert.Equal(1, spanCount);
    }

    [Then("the trace log shows (.*) images were processed")]
    public void ThenTraceLogShowsImagesProcessed(int expectedCount)
    {
        _tracerProvider?.ForceFlush();

        var traceLogPath = Path.Combine(_tempBase, TestMessageId.ToString(), "traces.log");
        var lines = File.ReadAllLines(traceLogPath);
        var autoCorrectCount = lines.Count(l => l.Contains("PdfCreator.AutoCorrect"));
        Assert.Equal(expectedCount, autoCorrectCount);
    }

    [Then("the trace log is written to the correct album directory")]
    public void ThenTraceLogIsWrittenToCorrectAlbumDirectory()
    {
        var expectedPath = Path.Combine(_tempBase, TestMessageId.ToString(), "traces.log");
        Assert.True(File.Exists(expectedPath),
            $"Expected trace log at {expectedPath} for AlbumId {TestMessageId}");
    }

    [Then("no PDF was created in the working directory")]
    public void ThenNoPdfWasCreated()
    {
        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var pdfFiles = Directory.GetFiles(workDir, "*.pdf");
        Assert.Empty(pdfFiles);
    }

    [Then("no files were published to the output directory")]
    public void ThenNoFilesWerePublished()
    {
        if (!Directory.Exists(_outputDir))
        {
            return;
        }

        var files = Directory.GetFiles(_outputDir);
        Assert.Empty(files);
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

        var maxWait = TimeSpan.FromSeconds(30);
        var deadline = DateTime.UtcNow + maxWait;

        while (DateTime.UtcNow < deadline)
        {
            if (_telegram.DashboardCalls.Any(c =>
                    c.AlbumId == _activeMessageId && c.Stage.StartsWith("❌", StringComparison.Ordinal)))
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail("Album did not fail within timeout.");
    }

    [Then("the bot replied with {string}")]
    public void ThenBotRepliedWith(string expectedStartsWith)
    {
        Assert.Contains(_telegram.Calls, c => c.Text.StartsWith(expectedStartsWith));
    }

    [Then("the bot replied with an error message")]
    public void ThenBotRepliedWithAnErrorMessage()
    {
        Assert.Contains(_telegram.Calls,
            c => !string.IsNullOrWhiteSpace(c.Text) && !c.Text.StartsWith("Done ", StringComparison.Ordinal));
    }

    [Then("the dashboard showed {string}")]
    public void ThenDashboardShowed(string expectedStartsWith)
    {
        Assert.Contains(_telegram.DashboardCalls,
            c => c.Stage.StartsWith(expectedStartsWith) && c.AlbumId == _activeMessageId);
    }

    [Then("the diagnostic was shown for album {int}")]
    public void ThenDiagnosticWasShownForAlbum(int albumId)
    {
        Assert.Contains(albumId, _telegram.DiagnosticAlbumIds);
    }

    [Then("the diagnostic was shown {int} times")]
    public void ThenDiagnosticWasShown(int count)
    {
        Assert.Equal(count, _telegram.DiagnosticAlbumIds.Count);
    }

    [Then("the LLM was called (.*) times")]
    public void ThenLlmWasCalledTimes(int expectedCount)
    {
        Assert.Equal(expectedCount, _fakeChatClient.InvocationCount);
    }

    [Then("the last bot reply starts with {string}")]
    public void ThenLastBotReplyStartsWith(string expectedStartsWith)
    {
        Assert.StartsWith(expectedStartsWith, _telegram.Calls.Last().Text);
    }

    [Then("the last bot reply contains {string}")]
    public void ThenLastBotReplyContains(string expectedContains)
    {
        Assert.Contains(expectedContains, _telegram.Calls.Last().Text);
    }

    [Then("the log content for the message contains logs and traces")]
    public void ThenLogContentContainsLogsAndTraces()
    {
        _tracerProvider?.ForceFlush();

        var logRetriever = _sp.GetRequiredService<ILogRetriever>();
        var logContent = logRetriever.GetLogContent(TestMessageId);

        Assert.Contains("AlbumProcessor.ExecuteAsync", logContent);
        Assert.Contains("📥 Processing started", logContent);
    }

    [Then("the log content is chronologically sorted")]
    public void ThenLogContentIsChronologicallySorted()
    {
        _tracerProvider?.ForceFlush();

        var logRetriever = _sp.GetRequiredService<ILogRetriever>();
        var logContent = logRetriever.GetLogContent(TestMessageId);

        _output.WriteLine("=== Log Content ===");
        _output.WriteLine(logContent);

        var lines = logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var timestamps = new List<TimeSpan>();
        foreach (var line in lines)
        {
            if (line.StartsWith("📋"))
            {
                continue;
            }

            if (line.Length >= 12 &&
                TimeSpan.TryParseExact(line[..12], @"hh\:mm\:ss\.fff", null, out var ts))
            {
                timestamps.Add(ts);
            }
        }

        for (var i = 1; i < timestamps.Count; i++)
        {
            Assert.True(timestamps[i] >= timestamps[i - 1],
                $"Log lines are not chronologically sorted at index {i}: {timestamps[i - 1]} > {timestamps[i]}");
        }
    }

    [Then("the log content uses short timestamp format")]
    public void ThenLogContentUsesShortTimestampFormat()
    {
        _tracerProvider?.ForceFlush();

        var logRetriever = _sp.GetRequiredService<ILogRetriever>();
        var logContent = logRetriever.GetLogContent(TestMessageId);

        _output.WriteLine("=== Log Content ===");
        _output.WriteLine(logContent);

        Assert.DoesNotContain("--- Traces ---", logContent);

        var lines = logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("📋"))
            {
                continue;
            }

            Assert.Matches(@"^\d{2}:\d{2}:\d{2}\.\d{3}", line);
        }
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
        Assert.Contains("PipelineService.RunAsync", joined);
        Assert.Contains("PdfCreator.CreatePdf", joined);
    }

    [AfterScenario]
    public async Task Cleanup()
    {
        _cts?.Cancel();
        _tracerProvider?.Dispose();
        Log.CloseAndFlush();
        if (_sp is IAsyncDisposable ad)
        {
            await ad.DisposeAsync();
        }
    }
}
