namespace Paperoni.Ai;

public interface IPromptProvider
{
    Task<string> GetPromptAsync(int albumId, CancellationToken ct = default);
}
