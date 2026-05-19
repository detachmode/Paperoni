namespace Paperoni.AlbumProcessing;

public class AlbumProcessingSettings
{
    public bool TestMode { get; set; }
    public string? TestModeOutputPath { get; set; }
    public string? MarkdownOutputPath { get; set; }
    public string? PdfPublisherOutputPath { get; set; }
}
