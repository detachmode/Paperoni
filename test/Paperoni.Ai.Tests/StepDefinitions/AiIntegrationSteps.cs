using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paperoni.Contract;
using Paperoni.Telegram;
using Reqnroll;
using Xunit.Abstractions;

namespace Paperoni.Ai.Tests.StepDefinitions;

[Binding]
public class AiIntegrationSteps
{
    private readonly ITestOutputHelper _output;
    private string? _answer;
    private readonly List<FileContent> _files = new();
    private readonly string _tempDir;

    public AiIntegrationSteps(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
    }

    private sealed class StubTelegramReplier : ITelegramReplier
    {
        public Task EditReply(int msgId, string text) => Task.CompletedTask;
        public Task ReplyError(int albumId, string errorMessage) => Task.CompletedTask;
        public Task SetReaction(int albumMsgId, string emoji) => Task.CompletedTask;
        public Task UpdateDashboard(int albumId, string stage, int queueDepth) => Task.CompletedTask;
        public Task DeleteDashboard() => Task.CompletedTask;
        public Task ShowDiagnostic(int albumId) => Task.CompletedTask;
        public Task ShowCropDetails(int albumId) => Task.CompletedTask;
    }

    [When("I ask {string}")]
    public async Task WhenIAsk(string question)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AlbumProcessing:ScriptFilePath"] = Path.Combine(_tempDir, "pipeline.csx"),
                ["Ai:Endpoint"] = "http://localhost:2276",
                ["Ai:Model"] = "test-model",
            })
            .Build();

        var scriptPath = Path.Combine(_tempDir, "pipeline.csx");
        await File.WriteAllTextAsync(scriptPath,
            """
            using System.ComponentModel;
            public record SimpleAnswer([property: Description("Answer")] string Answer);
            var Schema = typeof(SimpleAnswer);
            var Prompt = "Answer the question.";
            Func<SimpleAnswer, string> GetFilename = a => "test";
            Func<SimpleAnswer, string> Format = a => a.Answer;
            """);

        IServiceCollection serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(new WorkingDirectory { PaperoniWorkingDirectory = _tempDir });
        serviceCollection.AddSingleton<ITelegramReplier>(new StubTelegramReplier());
        serviceCollection.AddSingleton<IConfiguration>(config);
        serviceCollection.AddAiService(config);
        serviceCollection.AddSingleton<IScriptLoader, ScriptLoader>();
        var sp = serviceCollection.BuildServiceProvider();
        var pipelineService = sp.GetRequiredService<IPipelineService>();
        var scriptLoader = sp.GetRequiredService<IScriptLoader>();
        var script = await scriptLoader.LoadAsync(scriptPath, new ScriptGlobals([], DateTime.Now));
        var result = await pipelineService.RunAsync(script, 0, (_, _) => { });
        _answer = result.FormattedContent;
    }

    [When("I send two images: a red square and a blue circle")]
    public void WhenISendTwoImages()
    {
        var redImage = File.ReadAllBytes("Images/red.png");
        var blueImage = File.ReadAllBytes("Images/blue.png");

        _files.Add(new FileContent(redImage, "image/png"));
        _files.Add(new FileContent(blueImage, "image/png"));
    }

    [When("I ask {string} with the images")]
    public async Task WhenIAskWithImages(string question)
    {
        _answer = "placeholder";
    }

    [Then("the answer should contain {string}")]
    public void ThenAnswerShouldContain(string expected)
    {
        Assert.NotNull(_answer);
        _output.WriteLine($"AI response: {_answer}");
        Assert.Contains(expected, _answer, StringComparison.OrdinalIgnoreCase);
    }
}
