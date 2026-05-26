using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paperoni.Ai;
using Paperoni.Contract;
using Paperoni.Telegram;
using Reqnroll;
using Xunit.Abstractions;

namespace Paperoni.RealAi.Tests.StepDefinitions;

[Binding]
public class AiIntegrationSteps
{
    private readonly IPipelineService _pipelineService;
    private readonly string _tempDir;
    private readonly string _scriptPath;
    private readonly int _albumId = 1;
    private readonly ITestOutputHelper _output;
    private string? _answer;
    private string? _functionCallingAnswer;

    public AiIntegrationSteps(ITestOutputHelper output)
    {
        _output = output;
        _tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(Path.Combine(_tempDir, _albumId.ToString()));

        _scriptPath = Path.Combine(_tempDir, "pipeline.csx");
        File.WriteAllText(_scriptPath, @"will be filled via steps later");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ai:ScriptFilePath"] = _scriptPath,
                ["Ai:Endpoint"] = "http://localhost:2276",
                ["Ai:Model"] = "qwen-3.6-35b-a3b-q4",
            })
            .Build();

        IServiceCollection serviceCollection = new ServiceCollection();
        serviceCollection
            .AddSingleton<IConfiguration>(config)
            .AddLogging();
        serviceCollection.AddSingleton(new WorkingDirectory { PaperoniWorkingDirectory = _tempDir });
        serviceCollection.AddSingleton<ITelegramReplier>(new StubTelegramReplier());
        serviceCollection.AddAiService(config);
        serviceCollection.AddSingleton<IScriptLoader, ScriptLoader>();
        var sp = serviceCollection.BuildServiceProvider();
        _pipelineService = sp.GetRequiredService<IPipelineService>();
    }

    [When("I ask {string}")]
    public async Task WhenIAsk(string question)
    {
        await WriteScript(question);
        var result = await _pipelineService.RunAsync(_albumId, (type, msg) => _output.WriteLine($"[{type}]{msg}"));
        _answer = result.FormattedContent;
    }

    [When("I ask about the weather for a hike")]
    public async Task WhenIAskAboutTheWeatherForAHike()
    {
        await WriteScript("What is the current weather for a moderate hike in Montreal?");
        var result = await _pipelineService.RunAsync(_albumId, (type, msg) => _output.WriteLine($"[{type}]{msg}"));
        _functionCallingAnswer = result.FormattedContent;
        _output.WriteLine($"Function calling response: {_functionCallingAnswer}");
    }

    [Then("the answer should contain weather information")]
    public void ThenAnswerShouldContainWeatherInformation()
    {
        Assert.NotNull(_functionCallingAnswer);
        _output.WriteLine($"AI response: {_functionCallingAnswer}");
        Assert.Contains("rain", _functionCallingAnswer, StringComparison.OrdinalIgnoreCase);
        _output.WriteLine("Tool calling succeeded — AI invoked get_current_weather and used the result.");
    }

    [When("I send two images: a red square and a blue circle")]
    public void WhenISendTwoImages()
    {
        var redImage = File.ReadAllBytes("Images/red.png");
        var blueImage = File.ReadAllBytes("Images/blue.png");

        var albumDir = Path.Combine(_tempDir, _albumId.ToString());
        File.WriteAllBytes(Path.Combine(albumDir, "1.png"), redImage);
        File.WriteAllBytes(Path.Combine(albumDir, "2.png"), blueImage);
    }

    [When("I ask {string} with the images")]
    public async Task WhenIAskWithImages(string question)
    {
        await WriteScript(question);
        var result = await _pipelineService.RunAsync(_albumId, (type, msg) => _output.WriteLine($"[{type}]{msg}"));
        _answer = result.FormattedContent;
    }

    [Then("the answer should contain {string}")]
    public void ThenAnswerShouldContain(string expected)
    {
        Assert.NotNull(_answer);
        _output.WriteLine($"AI response: {_answer}");
        Assert.Contains(expected, _answer, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StubTelegramReplier : ITelegramReplier
    {
        public Task EditReply(int msgId, string text) => Task.CompletedTask;

        public Task ReplyError(int albumId, string errorMessage)
        {
            return Task.CompletedTask;
        }

        public Task SetReaction(int albumMsgId, string emoji) => Task.CompletedTask;
        public Task UpdateDashboard(int albumId, string stage, int queueDepth) => Task.CompletedTask;
        public Task DeleteDashboard() => Task.CompletedTask;
        public Task ShowDiagnostic(int albumId) => Task.CompletedTask;
    }

    private async Task WriteScript(string question)
    {
        var escapedQuestion = question.Replace("\"", "\\\"");
        await File.WriteAllTextAsync(_scriptPath,
            $$"""
              using System.ComponentModel;
              public record AiAnswer(
                [property: Description("Final answer text")] string Answer
              );
              var Schema = typeof(AiAnswer);

              var Prompt = "{{escapedQuestion}}";

              Func<AiAnswer, string> GetFilename = _ => "real-ai-test";
              Func<AiAnswer, string> Format = a => a.Answer;
              """);
    }
}
