using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Paperoni.Contract;
using Paperoni.Diagnostics;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Paperoni.Telegram;

public interface ITelegramReplier
{
    Task EditReply(int msgId, string text);
    Task ReplyError(int albumId, string errorMessage);
    Task SetReaction(int albumMsgId, string emoji);
    Task UpdateDashboard(int albumId, string stage, int queueDepth);
    Task DeleteDashboard();
    Task ShowDiagnostic(int albumId);
    Task ShowCropDetails(int albumId);
}

public class TelegramReplier(
    ITelegramBotClient bot,
    WorkingDirectory workingDirectory,
    ILogRetriever logRetriever) : ITelegramReplier
{
    private static readonly Regex s_timestampRegex = new(
        @"(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3})",
        RegexOptions.Compiled);

    private static readonly Regex s_cropStatusRegex = new(
        @"^✂️ Photo (?<photo>\d+): (?<status>.+)$",
        RegexOptions.Compiled);

    private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Dictionary<int, AlbumStatusCard> _albumStatuses = new();
    private DashboardMetadata? _cached;
    private bool _loaded;

    private string MetadataPath => Path.Combine(workingDirectory.BasePath, "Metadata.json");

    public async Task EditReply(int msgId, string text)
    {
        var metadata = await workingDirectory.RequireData<MetaData>(msgId);
        var chatId = metadata.ChatId;
        var replyMessageId = metadata.ReplyMessageId;
        ArgumentNullException.ThrowIfNull(replyMessageId);

        var markup = BuildAlbumMarkup(msgId);

        await bot.EditMessageText(chatId, replyMessageId.Value, text, replyMarkup: markup);
    }

    public async Task ReplyError(int albumId, string errorMessage)
    {
        try
        {
            await EditReply(albumId, errorMessage);
            return;
        }
        catch
        {
        }

        long chatId = 0;
        try
        {
            var metadata = await workingDirectory.RequireData<MetaData>(albumId);
            chatId = metadata.ChatId;
        }
        catch
        {
            await _lock.WaitAsync();
            try
            {
                var dashboard = await GetOrCreateMetadata();
                chatId = dashboard.ChatId;
            }
            finally
            {
                _lock.Release();
            }
        }

        if (chatId != 0)
        {
            await bot.SendMessage(chatId, errorMessage);
        }
    }

    public async Task SetReaction(int albumMsgId, string emoji)
    {
        var metadata = await workingDirectory.RequireData<MetaData>(albumMsgId);
        await bot.SetMessageReaction(metadata.ChatId, albumMsgId, [emoji]);
    }

    public async Task UpdateDashboard(int albumId, string stage, int queueDepth)
    {
        await _lock.WaitAsync();
        try
        {
            var metadata = await workingDirectory.RequireData<MetaData>(albumId);
            var replyMessageId = metadata.ReplyMessageId;
            if (replyMessageId is null)
            {
                return;
            }

            var card = GetStatusCard(albumId);
            card.Apply(stage, queueDepth);
            try
            {
                await bot.EditMessageText(metadata.ChatId, replyMessageId.Value, card.Render(),
                    replyMarkup: BuildAlbumMarkup(albumId));
            }
            catch
            {
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteDashboard()
    {
        await _lock.WaitAsync();
        try
        {
            var dm = await GetOrCreateMetadata();
            if (dm.DashboardMessageId is { } msgId && dm.ChatId != 0)
            {
                await SafeDeleteMessage(dm.ChatId, msgId);
            }

            dm.DashboardMessageId = null;
            dm.CurrentAlbumId = null;
            await SaveMetadata(dm);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ShowCropDetails(int albumId)
    {
        var metadata = await workingDirectory.RequireData<MetaData>(albumId);
        var lines = new List<string> { $"🧾 Album {albumId} — Crop details" };

        var workingDir = workingDirectory.RequireWorkingDirectory(albumId);
        var files = Directory.GetFiles(workingDir, "*.cropDecision.json")
            .OrderBy(Path.GetFileName)
            .ToList();

        if (files.Count == 0)
        {
            lines.Add("No crop decisions found yet.");
        }
        else
        {
            foreach (var file in files)
            {
                var summary = await FormatCropDecision(file);
                lines.Add(summary);
            }
        }

        var text = string.Join("\n", lines);
        const int textLimit = 3900;
        if (text.Length > textLimit)
        {
            text = text[..textLimit] + "\n...(truncated)";
        }

        var encoded = WebUtility.HtmlEncode(text);
        var markup = new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("✖️ Close", "close_diag")]
        ]);

        await bot.SendMessage(metadata.ChatId, $"<pre>{encoded}</pre>", parseMode: ParseMode.Html,
            replyMarkup: markup);
    }

    public async Task ShowDiagnostic(int albumId)
    {
        DashboardMetadata dm;
        try
        {
            dm = await GetOrCreateMetadata();
        }
        catch
        {
            dm = new DashboardMetadata();
        }

        var chatId = dm.ChatId;
        if (chatId == 0)
        {
            try
            {
                var albumMeta = await workingDirectory.RequireData<MetaData>(albumId);
                chatId = albumMeta.ChatId;
            }
            catch
            {
                return;
            }
        }

        var lines = new List<string> { $"📊 Album {albumId} — Diagnosis" };

        var aiResult = await workingDirectory.GetData<PipelineResult>(albumId);
        if (aiResult?.Filename is not null)
        {
            lines.Add($"Title: \"{aiResult.Filename}\"");
        }

        var duration = await GetTraceDuration(albumId);
        if (duration.HasValue)
        {
            lines.Add($"Completed in {duration.Value.TotalSeconds:F1}s");
        }

        lines.Add("---");

        var logContent = logRetriever.GetLogContent(albumId);
        if (!string.IsNullOrEmpty(logContent))
        {
            var logLines = logContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            lines.AddRange(logLines.Skip(1));
        }
        else
        {
            lines.Add("No logs found.");
        }

        var text = string.Join("\n", lines);

        const int textLimit = 3900;
        if (text.Length > textLimit)
        {
            text = text[..textLimit] + "\n...(truncated)";
        }

        var encoded = WebUtility.HtmlEncode(text);

        await _lock.WaitAsync();
        try
        {
            dm = await GetOrCreateMetadata();

            if (dm.DiagnosticMessageId is { } oldDiagId)
            {
                await SafeDeleteMessage(chatId, oldDiagId);
            }

            var markup = new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("✖️ Close", "close_diag")]
            ]);

            var sent = await bot.SendMessage(chatId, $"<pre>{encoded}</pre>", parseMode: ParseMode.Html,
                replyMarkup: markup);
            dm.DiagnosticMessageId = sent.MessageId;
            dm.ChatId = chatId;
            await SaveMetadata(dm);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<DashboardMetadata> GetOrCreateMetadata()
    {
        if (!_loaded)
        {
            var path = MetadataPath;
            if (File.Exists(path))
            {
                var json = await File.ReadAllTextAsync(path);
                _cached = JsonSerializer.Deserialize<DashboardMetadata>(json);
            }

            _cached ??= new DashboardMetadata();
            _loaded = true;
        }

        return _cached!;
    }

    private AlbumStatusCard GetStatusCard(int albumId)
    {
        if (!_albumStatuses.TryGetValue(albumId, out var card))
        {
            card = new AlbumStatusCard(albumId);
            _albumStatuses[albumId] = card;
        }

        return card;
    }

    private static InlineKeyboardMarkup BuildAlbumMarkup(int albumId) => new([
        [
            InlineKeyboardButton.WithCallbackData("🔄 Retry", $"retry:{albumId}"),
            InlineKeyboardButton.WithCallbackData("✂️ LLM recrop", $"recrop:{albumId}")
        ],
        [
            InlineKeyboardButton.WithCallbackData("📋 Logs", $"logs:{albumId}"),
            InlineKeyboardButton.WithCallbackData("🧾 Crop details", $"crop:{albumId}")
        ]
    ]);

    private static async Task<string> FormatCropDecision(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(path));
            var root = doc.RootElement;
            var sourceFile = root.GetProperty("sourceFile").GetString() ?? Path.GetFileName(path);
            var strategy = root.GetProperty("finalStrategy").GetString() ?? "Unknown";
            var reason = root.GetProperty("reason").GetString() ?? "";
            var openCv = root.GetProperty("openCv");
            var confidence = openCv.GetProperty("confidence").GetString() ?? "Unknown";
            var score = openCv.GetProperty("score").GetDouble();

            var icon = strategy switch
            {
                "OpenCv" => "🟢",
                "Llm" => "🟢",
                _ => "⚠️"
            };

            return $"{icon} {sourceFile}: {strategy} — OpenCV {confidence} {score:F2} — {reason}";
        }
        catch (Exception ex)
        {
            return $"⚠️ {Path.GetFileName(path)}: could not read crop decision ({ex.Message})";
        }
    }

    private sealed class AlbumStatusCard(int albumId)
    {
        private readonly DateTime _startedAt = DateTime.Now;
        private readonly SortedDictionary<int, string> _photos = new();

        private string _current = "Queued";
        private string _summary = "⏳ pending";
        private string _pdf = "⏳ pending";
        private string _publish = "⏳ pending";
        private int _queueDepth;

        public void Apply(string stage, int queueDepth)
        {
            _queueDepth = queueDepth;
            _current = SimplifyStage(stage);

            if (stage.StartsWith("🤖", StringComparison.Ordinal))
            {
                _summary = stage.Contains("done", StringComparison.OrdinalIgnoreCase) ? "✅ done" : "⏳ running";
            }
            else if (stage.StartsWith("📄", StringComparison.Ordinal))
            {
                _summary = "✅ done";
                _pdf = "⏳ creating";
            }
            else if (stage.StartsWith("✂️", StringComparison.Ordinal))
            {
                _summary = "✅ done";
                _pdf = "✂️ cropping";

                var match = s_cropStatusRegex.Match(stage);
                if (match.Success && int.TryParse(match.Groups["photo"].Value, out var photo))
                {
                    _photos[photo] = FormatPhotoStatus(match.Groups["status"].Value);
                }
            }
            else if (stage.StartsWith("📤", StringComparison.Ordinal))
            {
                _summary = "✅ done";
                _pdf = "✅ done";
                _publish = "⏳ publishing";
            }
            else if (stage.StartsWith("❌", StringComparison.Ordinal))
            {
                _current = stage;
            }
        }

        public string Render()
        {
            var elapsed = DateTime.Now - _startedAt;
            var lines = new List<string>
            {
                $"🤖 Paperoni Album {albumId}",
                $"Status: {_current}",
                $"Elapsed: {FormatDuration(elapsed)}",
                "",
                $"AI summary: {_summary}",
                $"PDF: {_pdf}",
                $"Publish: {_publish}"
            };

            if (_queueDepth > 0)
            {
                lines.Add($"Queue: {_queueDepth} pending");
            }

            if (_photos.Count > 0)
            {
                lines.Add("");
                lines.Add("Photos:");
                lines.AddRange(_photos.Select(p => $"{p.Key}. {p.Value}"));
            }

            return string.Join("\n", lines);
        }

        private static string SimplifyStage(string stage) => stage switch
        {
            var s when s.StartsWith("🤖 AI is reading", StringComparison.Ordinal) => "🤖 Reading Photos",
            var s when s.StartsWith("🤖 AI is thinking", StringComparison.Ordinal) => "🤖 AI thinking",
            var s when s.StartsWith("🤖 AI is formulating", StringComparison.Ordinal) => "🤖 Writing summary",
            var s when s.StartsWith("📄", StringComparison.Ordinal) => "📄 Creating PDF",
            var s when s.StartsWith("📤", StringComparison.Ordinal) => "📤 Publishing files",
            var s when s.StartsWith("✂️", StringComparison.Ordinal) => "✂️ Cropping Photos",
            _ => stage
        };

        private static string FormatPhotoStatus(string status)
        {
            var icon = status.Contains("High", StringComparison.OrdinalIgnoreCase) && status.Contains("kept", StringComparison.OrdinalIgnoreCase)
                ? "🟢"
                : status.Contains("LLM crop", StringComparison.OrdinalIgnoreCase)
                    ? "🟢"
                    : status.Contains("NoCrop", StringComparison.OrdinalIgnoreCase)
                        ? "⚠️"
                        : "🟡";

            return icon + " " + status
                .Replace("OpenCV ", "OpenCV ", StringComparison.Ordinal)
                .Replace("->", "→", StringComparison.Ordinal);
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalMinutes >= 1)
            {
                return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
            }

            return $"{duration.Seconds}s";
        }
    }

    private async Task SaveMetadata(DashboardMetadata dm)
    {
        _cached = dm;
        var json = JsonSerializer.Serialize(dm, s_jsonOptions);
        await File.WriteAllTextAsync(MetadataPath, json);
    }

    private async Task SafeDeleteMessage(long chatId, int messageId)
    {
        try
        {
            await bot.DeleteMessage(chatId, messageId);
        }
        catch
        {
        }
    }

    private async Task<TimeSpan?> GetTraceDuration(int albumId)
    {
        var traceDir = workingDirectory.RequireWorkingDirectory(albumId);
        var tracePath = Path.Combine(traceDir, "traces.log");
        if (!File.Exists(tracePath))
        {
            return null;
        }

        var lines = await File.ReadAllLinesAsync(tracePath);
        if (lines.Length < 2)
        {
            return null;
        }

        var first = ParseTimestamp(lines[0]);
        var last = ParseTimestamp(lines[^1]);
        if (first is null || last is null)
        {
            return null;
        }

        return last.Value - first.Value;
    }

    private static DateTime? ParseTimestamp(string line)
    {
        var match = s_timestampRegex.Match(line);
        if (!match.Success)
        {
            return null;
        }

        if (DateTime.TryParseExact(match.Value, "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt;
        }

        return null;
    }
}
