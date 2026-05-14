using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Paperoni.Ai;
using Paperoni.AlbumProcessing;
using Paperoni.Contract;
using Paperoni.ImageProcessing;
using Paperoni.Obsidian;
using Paperoni.Telegram;
using Paperoni.Telegram.Album;
using Reqnroll;
using Xunit.Abstractions;

namespace Paperoni.Tests.StepDefinitions;

[Binding]
public class AlbumProcessingSteps
{
    private readonly ITestOutputHelper _output;
    private string _tempBase = null!;
    private string _obsidianDir = null!;
    private string _driveDir = null!;
    private string _promptFilePath = null!;
    private ServiceProvider _sp = null!;
    private AlbumQueue _queue = null!;
    private FakeTelegramReplier _telegram = null!;
    private CancellationTokenSource? _cts;
    private const int TestMessageId = 42;

    public AlbumProcessingSteps(ITestOutputHelper output)
    {
        _output = output;
    }

    [Given("the system is configured for integration testing")]
    public async Task GivenSystemIsConfigured()
    {
        _tempBase = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        _obsidianDir = Path.Combine(_tempBase, "obsidian-output");
        _driveDir = Path.Combine(_tempBase, "gdrive-output");
        Directory.CreateDirectory(_tempBase);
        Directory.CreateDirectory(_obsidianDir);
        Directory.CreateDirectory(_driveDir);
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
        _queue = new AlbumQueue();
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
                ["ObsidianOutputPath"] = _obsidianDir,
                ["GoogleDriveOutputPath"] = _driveDir,
            })
            .Build();

        _output.WriteLine($"Test output directory: {_tempBase}");
        _output.WriteLine($"Obsidian output: {_obsidianDir}");
        _output.WriteLine($"Google Drive output: {_driveDir}");

        var services = new ServiceCollection();
        services.AddSingleton(_queue);
        services.AddSingleton<AlbumWorkingDirectory>(_ => new AlbumWorkingDirectory { DownloadBasePath = _tempBase });
        services.AddSingleton<ITelegramReplier>(_telegram);
        services.AddAiService();
        services.AddImageProcessing();
        services.AddObsidianStore();
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
        _queue.Enqueue(new AlbumQueueEntry(photos));
    }

    [Then("the album is processed")]
    public async Task ThenAlbumIsProcessed()
    {
        _cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var hostedServices = _sp.GetServices<IHostedService>();
        foreach (var service in hostedServices)
            await service.StartAsync(_cts.Token);

        await _telegram.WaitForCompletionAsync(_cts.Token);

        var workDir = Path.Combine(_tempBase, TestMessageId.ToString());
        var content = await File.ReadAllTextAsync(Path.Combine(workDir, "firstAiResponse.md"));
        _output.WriteLine($"AI Summary:\n{content}");
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
        var obsidianFiles = Directory.GetFiles(_obsidianDir, "*.md");
        Assert.NotEmpty(obsidianFiles);
    }

    [Then("the PDF is published to Google Drive")]
    public void ThenPdfPublishedToGoogleDrive()
    {
        var driveFiles = Directory.GetFiles(_driveDir, "*.pdf");
        Assert.NotEmpty(driveFiles);
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

    [AfterScenario]
    public async Task Cleanup()
    {
        _cts?.Cancel();
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
