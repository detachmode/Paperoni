using Paperoni.Contract;

namespace Paperoni.Ai;

public interface IPromptProvider
{
    Task<string> GetPromptAsync(int msgId, CancellationToken ct = default);
}

