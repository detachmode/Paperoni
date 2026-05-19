using Paperoni.Contract;

namespace Paperoni;

internal static class SplashScreen
{
    private const string AnsiReset = "\x1b[0m";

    private static readonly string[] s_banner =
    [
        "███████   ███   ███████  ████████ ███████   █████  ██    █ ███████",
        "██     █ █   █  ██     █ ██       ██     █ █     █ ███   █    █",
        "███████ █     █ ███████  ██       ███████  █     █ ██ █  █    █",
        "██      ███████ ██       ██████   ██   █   █     █ ██  █ █    █",
        "██      █     █ ██       ██       ██    █  █     █ ██   ██    █",
        "██      █     █ ██       ████████ ██     █  █████  ██    █ ███████",
    ];

    private static readonly int s_bannerWidth = s_banner[0].Length;

    public static void Render()
    {
        int windowWidth = Math.Max(Console.WindowWidth, 0);
        if (windowWidth < s_bannerWidth)
        {
            Console.WriteLine($"Paperoni v{VersionInfo.Version}");
            Console.WriteLine($"         Sha: {VersionInfo.CommitSha}");
            Console.WriteLine();
            return;
        }

        int leftPad = (windowWidth - s_bannerWidth) / 2;
        string pad = new(' ', leftPad);

        Console.WriteLine();

        for (int i = 0; i < s_banner.Length; i++)
        {
            float t = (float)i / (s_banner.Length - 1);
            Console.Write(GradientAnsi(t));
            Console.WriteLine($"{pad}{s_banner[i]}");
        }

        int sepWidth = Math.Min(s_bannerWidth, windowWidth - 2 * leftPad);
        string sep = new('─', sepWidth);
        const float midT = 0.5f;

        Console.Write(GradientAnsi(midT));
        Console.WriteLine($"{pad}{sep}");

        string version = $"v{VersionInfo.Version}";
        int versionPad = (windowWidth - version.Length) / 2;
        string vPad = new(' ', Math.Max(0, versionPad));
        Console.Write(GradientAnsi(midT));
        Console.Write(vPad);
        Console.WriteLine(version);

        Console.Write(AnsiReset);
        Console.WriteLine();
    }

    private static string GradientAnsi(float t)
    {
        int r = (int)(200 * (1 - t) + 0 * t);
        int g = (int)(80 * (1 - t) + 200 * t);
        int b = (int)(255 * (1 - t) + 220 * t);
        return $"\x1b[38;2;{r};{g};{b}m";
    }
}
