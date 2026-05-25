using Paperoni.Telegram;
using Reqnroll;

namespace Paperoni.RealAi.Tests;

public class FakeTelegramReplier(IReqnrollOutputHelper outputHelper) : ITelegramReplier
{
    private TaskCompletionSource _done = new();
    private readonly List<(int MsgId, string Text)> _calls = [];
    private readonly object _lock = new();
    public readonly List<string> DashboardUpdates = [];

    public Task EditReply(int msgId, string text)
    {
        lock (_lock)
        {
            _calls.Add((msgId, text));
        }

        if (text.StartsWith("❌"))
        {
            _done.SetCanceled();
        }

        if (text.StartsWith("✅"))
        {
            _done.TrySetResult();
        }

        return Task.CompletedTask;
    }

    public Task ReplyError(int albumId, string errorMessage)
    {
        return Task.CompletedTask;
    }

    public Task SetReaction(int albumMsgId, string emoji) => Task.CompletedTask;

    public Task UpdateDashboard(int albumId, string stage, int queueDepth)
    {
        lock (_lock)
        {
            outputHelper.WriteLine("Dashboard update:" +stage);
            DashboardUpdates.Add(stage);
        }

        return Task.CompletedTask;
    }

    public Task DeleteDashboard() => Task.CompletedTask;
    public Task ShowDiagnostic(int albumId) => Task.CompletedTask;

    public void Reset()
    {
        _done = new TaskCompletionSource();
    }

    public Task WaitForCompletionAsync(CancellationToken ct = default, int timeoutSeconds = 60) =>
        _done.Task.WaitAsync(TimeSpan.FromSeconds(timeoutSeconds), ct).WaitAsync(ct);

    public IReadOnlyList<(int MsgId, string Text)> Calls
    {
        get
        {
            lock (_lock)
            {
                return _calls.ToList();
            }
        }
    }
}
