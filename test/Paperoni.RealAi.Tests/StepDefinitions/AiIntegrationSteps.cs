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
    private readonly IAiService _aiService;
    private readonly ITestOutputHelper _output;
    private string? _answer;
    private string? _functionCallingAnswer;
    private readonly List<FileContent> _files = new();

    public AiIntegrationSteps(ITestOutputHelper output)
    {
        _output = output;
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);

        var promptFile = Path.Combine(tempDir, "prompt.md");
        File.WriteAllText(promptFile, "Answer the question.");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PromptFilePath"] = promptFile,
            })
            .Build();

        IServiceCollection serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        serviceCollection.AddSingleton(new AlbumWorkingDirectory { DownloadBasePath = tempDir });
        serviceCollection.AddSingleton<ITelegramReplier>(new StubTelegramReplier());
        serviceCollection.AddSingleton<IConfiguration>(config);
        serviceCollection.AddAiService();
        var sp = serviceCollection.BuildServiceProvider();
        _aiService = sp.GetRequiredService<IAiService>();
    }

    private sealed class StubTelegramReplier : ITelegramReplier
    {
        public Task EditReply(int msgId, string text) => Task.CompletedTask;
        public Task SetReaction(int albumMsgId, string emoji) => Task.CompletedTask;
    }

    [When("I ask {string}")]
    public async Task WhenIAsk(string question)
    {
        _answer = await _aiService.AskWithFilesAsync([], question, (type, msg) => _output.WriteLine($"[{type}]{msg}"));
    }

    [When("I ask about the weather for a hike")]
    public async Task WhenIAskAboutTheWeatherForAHike()
    {
        _functionCallingAnswer = await _aiService.TryFunctionCalling();
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

        _files.Add(new FileContent(redImage, "image/png"));
        _files.Add(new FileContent(blueImage, "image/png"));
    }

    [When("I ask {string} with the images")]
    public async Task WhenIAskWithImages(string question)
    {
        _answer = await _aiService.AskWithFilesAsync(_files, question, (type, msg) => _output.WriteLine($"[{type}]{msg}"));
    }

    [Then("the answer should contain {string}")]
    public void ThenAnswerShouldContain(string expected)
    {
        Assert.NotNull(_answer);
        _output.WriteLine($"AI response: {_answer}");
        Assert.Contains(expected, _answer, StringComparison.OrdinalIgnoreCase);
    }
}
