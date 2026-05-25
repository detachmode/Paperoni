Feature: Album Processing Smoke (Real AI)
	As a user sending photos to Telegram
	I want albums to be processed end-to-end with the real AI
	So that I can verify the full integration works

	Scenario: Single photo album is processed end-to-end with real AI
		Given the system is configured for real AI integration testing
		And the pipeline script is:
		"""
using System.ComponentModel;
using Paperoni.Ai;

public record AlbumNote(
    [property: Description("Title of the document")]
    string Title,

    [property: Description("Short summary")]
    string Summary,

    [property: Description("Full content in markdown")]
    string MarkdownBody
);

var Schema = typeof(AlbumNote);

var Prompt = "Analyse the following document and extract the text. Return a short summary and the complete visible text as markdown.";


string GetFilename(AlbumNote note)
{
    var safe = MarkdownHelper.AutoFixDate(note.Title ?? "Unknown");
    return MarkdownHelper.SanitizeFilename(safe);
}

string Format(AlbumNote note)
{
    var filename = GetFilename(note);
    return "---\ntitle: " + filename + "\n---\n\n# " + note.Summary + "\n\n" + note.MarkdownBody;
}
		"""
		And the real AI processing pipeline is built
		When I enqueue a photo with caption "Test document"
		Then the album is processed with real AI
		And a PDF is created
		And the summary is published to Obsidian
		And the PDF is published to the output directory
		And the trace log contains expected traces
