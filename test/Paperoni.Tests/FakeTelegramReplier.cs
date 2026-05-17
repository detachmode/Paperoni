using Paperoni.Telegram;

namespace Paperoni.Tests;

public class FakeTelegramReplier : ITelegramReplier
{
    private TaskCompletionSource _done = new();
    private readonly List<(int MsgId, string Text)> _calls = [];
    private readonly List<(int MsgId, string Emoji)> _reactions = [];
    private readonly object _lock = new();

    public Task EditReply(int msgId, string text)
    {
        lock (_lock)
        {
            _calls.Add((msgId, text));
        }
        if (text.StartsWith("Done:"))
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

    public void Reset()
    {
        _done = new TaskCompletionSource();
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
}
