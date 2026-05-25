namespace Paperoni.Telegram;

public sealed class NoOpTelegramReplier : ITelegramReplier
{
    public Task EditReply(int msgId, string text) => Task.CompletedTask;

    public Task ReplyError(int albumId, string errorMessage) => Task.CompletedTask;

    public Task SetReaction(int albumMsgId, string emoji) => Task.CompletedTask;

    public Task UpdateDashboard(int albumId, string stage, int queueDepth) => Task.CompletedTask;

    public Task DeleteDashboard() => Task.CompletedTask;

    public Task ShowDiagnostic(int albumId) => Task.CompletedTask;
}
