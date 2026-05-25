using Paperoni.Telegram;

namespace Paperoni.Tests;

public class FakeTelegramReplier : ITelegramReplier
{
    private TaskCompletionSource _done = new();
    private readonly List<(int MsgId, string Text)> _calls = [];
    private readonly List<(int MsgId, string Emoji)> _reactions = [];
    private readonly List<(int AlbumId, string Stage)> _dashboardCalls = [];
    private readonly List<int> _diagnosticAlbumIds = [];
    private int _deleteDashboardCount;
    private readonly object _lock = new();

    public Task EditReply(int msgId, string text)
    {
        lock (_lock)
        {
            _calls.Add((msgId, text));
        }
        if (text.StartsWith("✅ Done "))
        {
            _done.TrySetResult();
        }
        return Task.CompletedTask;
    }

    public Task SetReaction(int albumMsgId, string emoji)
    {
        lock (_lock)
        {
            _reactions.Add((albumMsgId, emoji));
        }
        return Task.CompletedTask;
    }

    public Task ReplyError(int albumId, string errorMessage) => EditReply(albumId, errorMessage);

    public Task UpdateDashboard(int albumId, string stage, int queueDepth)
    {
        lock (_lock)
        {
            _dashboardCalls.Add((albumId, stage));
        }
        return Task.CompletedTask;
    }

    public Task DeleteDashboard()
    {
        Interlocked.Increment(ref _deleteDashboardCount);
        return Task.CompletedTask;
    }

    public Task ShowDiagnostic(int albumId)
    {
        lock (_lock)
        {
            _diagnosticAlbumIds.Add(albumId);
        }
        return Task.CompletedTask;
    }

    public void Reset()
    {
        _done = new TaskCompletionSource();
        _calls.Clear();
        _reactions.Clear();
        _dashboardCalls.Clear();
        _diagnosticAlbumIds.Clear();
        _deleteDashboardCount = 0;
    }

    public Task WaitForCompletionAsync(CancellationToken ct = default) =>
        _done.Task.WaitAsync(ct);

    public IReadOnlyList<(int MsgId, string Text)> Calls
    {
        get { lock (_lock) { return _calls.ToList(); } }
    }

    public IReadOnlyList<(int MsgId, string Emoji)> Reactions
    {
        get { lock (_lock) { return _reactions.ToList(); } }
    }

    public IReadOnlyList<(int AlbumId, string Stage)> DashboardCalls
    {
        get { lock (_lock) { return _dashboardCalls.ToList(); } }
    }

    public IReadOnlyList<int> DiagnosticAlbumIds
    {
        get { lock (_lock) { return _diagnosticAlbumIds.ToList(); } }
    }

    public int DeleteDashboardCount => _deleteDashboardCount;
}
