using System.ComponentModel;
using Paperoni.Ai;

public record TestNote(
    [property: Description("Title")]
    string Title,

    [property: Description("Summary")]
    string Summary,

    [property: Description("Full content in markdown")]
    string MarkdownBody
);

var Schema = typeof(TestNote);

var Prompt = "Analyse the document.";

Func<TestNote, string> GetFilename = note => note.Title;

Func<TestNote, string> Format = note =>
{
    var filename = GetFilename(note);
    return "---\ntitle: " + filename + "\n---\n\n# " + note.Summary + "\n\n" + note.MarkdownBody;
};