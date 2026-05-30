namespace Paperoni.ImageProcessing;

public interface IPdfCreator
{
    Task CreatePdf(int messageId, Func<string, Task>? statusCallback = null, bool forceLlmCrop = false,
        CancellationToken cancellationToken = default);
}
