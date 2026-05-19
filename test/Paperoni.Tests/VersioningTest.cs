using Paperoni.Contract;
using Paperoni.Telegram;

namespace Paperoni.Tests;

public class VersioningTest
{
    [Fact]
    public void VersionInfo_Version_IsNotEmpty()
    {
        Assert.NotEmpty(VersionInfo.Version);
    }

    [Fact]
    public void VersionInfo_CommitSha_IsNotEmpty()
    {
        Assert.NotEmpty(VersionInfo.CommitSha);
    }

    [Fact]
    public void VersionInfo_BuildTime_IsNotEmpty()
    {
        Assert.NotEmpty(VersionInfo.BuildTime);
    }

    [Fact]
    public void VersionInfo_Version_FollowsSemVer()
    {
        Assert.Matches(@"^\d+\.\d+\.\d+", VersionInfo.Version);
    }

    [Fact]
    public void VersionCommand_ContainsExpectedParts()
    {
        var result = CommandResponses.Version();

        Assert.Contains("Paperoni ", result);
        Assert.Contains(VersionInfo.Version, result);
        Assert.Contains(VersionInfo.CommitSha, result);
        Assert.Contains(VersionInfo.BuildTime, result);
        Assert.Contains("UTC on ", result);
    }

    [Fact]
    public void HelpCommand_ContainsCommands()
    {
        var result = CommandResponses.Help();

        Assert.Contains("/version", result);
        Assert.Contains("/help", result);
    }
}
