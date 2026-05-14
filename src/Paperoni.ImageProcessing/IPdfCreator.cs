namespace Paperoni.ImageProcessing;

public interface IPdfCreator
{
    Task CreatePdf(int messageId, CancellationToken cancellationToken = default);
}
