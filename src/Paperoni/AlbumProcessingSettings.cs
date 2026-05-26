namespace Paperoni;

public class AlbumProcessingSettings
{
    public bool TestMode { get; set; }
    public string? TestModeOutputPath { get; set; }
    public string? MarkdownOutputPath { get; set; }
    public string? PdfOutputPath { get; set; }
    public int WorkingDirectoryRetentionDays { get; set; }
}
