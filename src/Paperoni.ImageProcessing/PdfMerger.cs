using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Paperoni.ImageProcessing;

internal sealed class PdfMerger 
{
    static PdfMerger()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    internal static byte[] MergeToPdf(IEnumerable<byte[]> images)
    {
        return Document.Create(container =>
        {
            foreach (var imageBytes in images)
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(1, Unit.Centimetre);
                    page.Content().Image(imageBytes).FitArea();
                });
            }
        }).GeneratePdf();
    }
}
