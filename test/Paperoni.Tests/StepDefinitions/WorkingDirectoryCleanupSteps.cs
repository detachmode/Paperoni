using Microsoft.Extensions.Logging.Abstractions;
using Paperoni.Contract;
using Reqnroll;

namespace Paperoni.Tests.StepDefinitions;

[Binding]
public class WorkingDirectoryCleanupSteps
{
    private string _tempBase = null!;
    private WorkingDirectory _workingDirectory = null!;
    private AlbumProcessingSettings _settings = null!;
    private WorkingDirectoryCleanup _cleanup = null!;
    private readonly List<int> _albumIds = [];

    [Given(@"the working directory contains album ""(.*)"" last modified (.*) days ago")]
    public void GivenAlbumDirectoryWithAge(int albumId, int daysAgo)
    {
        _tempBase ??= Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempBase);

        _workingDirectory = new WorkingDirectory { PaperoniWorkingDirectory = _tempBase };
        var dir = _workingDirectory.RequireWorkingDirectory(albumId);
        File.WriteAllText(Path.Combine(dir, "MetaData.json"), "{}");

        var pastTime = DateTime.UtcNow.AddDays(-daysAgo);
        Directory.SetLastWriteTimeUtc(dir, pastTime);
        _albumIds.Add(albumId);
    }

    [Given("the working directory contains a non-numeric directory {string}")]
    public void GivenNonNumericDirectory(string name)
    {
        _tempBase ??= Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempBase);

        _workingDirectory = new WorkingDirectory { PaperoniWorkingDirectory = _tempBase };
        Directory.CreateDirectory(Path.Combine(_tempBase, name));
    }

    [Given(@"the working directory retention is (.*) days")]
    public void GivenRetentionDays(int days)
    {
        _settings = new AlbumProcessingSettings
        {
            WorkingDirectoryRetentionDays = days,
            MarkdownOutputPath = "/tmp",
            PdfOutputPath = "/tmp",
        };

        _cleanup = new WorkingDirectoryCleanup(
            _workingDirectory,
            NullLogger<WorkingDirectoryCleanup>.Instance,
            _settings);
    }

    [When("cleanup runs")]
    public async Task WhenCleanupRuns()
    {
        await _cleanup.RunCleanup();
    }

    [Then(@"album directory ""(.*)"" is deleted")]
    public void ThenDirectoryIsDeleted(int albumId)
    {
        var path = Path.Combine(_tempBase, albumId.ToString());
        Assert.False(Directory.Exists(path), $"Expected {path} to be deleted");
    }

    [Then(@"album directory ""(.*)"" still exists")]
    public void ThenDirectoryStillExists(int albumId)
    {
        var path = Path.Combine(_tempBase, albumId.ToString());
        Assert.True(Directory.Exists(path), $"Expected {path} to still exist");
    }

    [Then("directory {string} still exists")]
    public void ThenNonNumericDirectoryStillExists(string name)
    {
        var path = Path.Combine(_tempBase, name);
        Assert.True(Directory.Exists(path), $"Expected {path} to still exist");
    }

    [AfterScenario]
    public void Cleanup()
    {
        if (_tempBase != null && Directory.Exists(_tempBase))
        {
            Directory.Delete(_tempBase, recursive: true);
        }
    }
}
