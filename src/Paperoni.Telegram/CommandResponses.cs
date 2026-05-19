using System.Runtime.InteropServices;
using Paperoni.Contract;

namespace Paperoni.Telegram;

public static class CommandResponses
{
    public static string Version()
    {
        var rid = RuntimeInformation.RuntimeIdentifier;
        return $"Paperoni {VersionInfo.Version} ({VersionInfo.CommitSha}) — built {VersionInfo.BuildTime} UTC on {rid}";
    }

    public static string Help()
    {
        return "Commands:\n/version — Show version info\n/help — Show this help" +
               "\n\nSend photos to create AI summaries and PDFs." +
               "\nInline buttons on album replies: 🔄 Retry, 📋 Logs";
    }
}
