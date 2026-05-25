using Paperoni.Ai;
using Paperoni.AlbumProcessing;

namespace Paperoni.Tests;

public class ScriptLoaderTests
{
    private readonly ScriptLoader _loader = new();

    private static string CreateScriptFile(string content, string extension = "csx")
    {
        var path = Path.Combine(Path.GetTempPath(), $"test-script-{Guid.NewGuid()}.{extension}");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task LoadAsync_ValidScript_ReturnsPipelineScript()
    {
        var script = """
            public record TestNote(
                [property: System.ComponentModel.Description("Title")]
                string Title,
                string Summary,
                string MarkdownBody
            );

            var Schema = typeof(TestNote);

            var Prompt = "Analyse the document.";

            string GetFilename(TestNote note) => note.Title.Replace(":", " -");

            string Format(TestNote note) => $"---\ntitle: {note.Title}\n---\n{note.MarkdownBody}";

            """;
        var path = CreateScriptFile(script);

        try
        {
            var result = await _loader.LoadAsync(path);

            Assert.NotNull(result);
            Assert.NotNull(result.Schema);
            Assert.Equal("Analyse the document.", result.Prompt);
            Assert.NotNull(result.GetFilenameDelegate);
            Assert.NotNull(result.FormatDelegate);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MissingSchema_ThrowsInvalidPipelineScriptException()
    {
        var script = """
            var Prompt = "Analyse the document.";

            Func<object, string> GetFilename = note => "fallback";

            Func<object, string> Format = note => note.ToString() ?? "";
            """;
        var path = CreateScriptFile(script);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidPipelineScriptException>(
                () => _loader.LoadAsync(path));
            Assert.Equal("Invalid Pipeline script: Schema variable must be set", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MissingPrompt_ThrowsInvalidPipelineScriptException()
    {
        var script = """
            public record TestNote(string Title);

            var Schema = typeof(TestNote);

            string GetFilename(TestNote note) => note.Title;

            string Format(TestNote note) => note.Title;
            """;
        var path = CreateScriptFile(script);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidPipelineScriptException>(
                () => _loader.LoadAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MissingGetFilename_ThrowsInvalidPipelineScriptException()
    {
        var script = """
            public record TestNote(string Title);

            var Schema = typeof(TestNote);

            var Prompt = "Analyse.";

            string Format(TestNote note) => note.Title;
            """;
        var path = CreateScriptFile(script);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidPipelineScriptException>(
                () => _loader.LoadAsync(path));
            Assert.Contains("compile error", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MissingFormat_ThrowsInvalidPipelineScriptException()
    {
        var script = """
            public record TestNote(string Title);

            var Schema = typeof(TestNote);

            var Prompt = "Analyse.";

            string GetFilename(TestNote note) => note.Title;
            """;
        var path = CreateScriptFile(script);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidPipelineScriptException>(
                () => _loader.LoadAsync(path));
            Assert.Contains("compile error", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MissingMultipleConventions_ReportsAllInMessage()
    {
        var script = """
            var Schema = typeof(string);

            var Prompt = "Analyse.";
            """;
        var path = CreateScriptFile(script);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidPipelineScriptException>(
                () => _loader.LoadAsync(path));
            Assert.Contains("GetFilename", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_CompileError_ThrowsInvalidPipelineScriptException()
    {
        var script = "this is not valid C#;";
        var path = CreateScriptFile(script);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidPipelineScriptException>(
                () => _loader.LoadAsync(path));
            Assert.NotEmpty(ex.Message);
            Assert.Contains("CS", ex.Message);
            Assert.Contains("line 1", ex.Message);
            Assert.Contains("this is not valid C#;", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_NonExistentFile_ThrowsFileNotFoundException()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(
            () => _loader.LoadAsync("/nonexistent/path/script.csx"));
    }

    [Fact]
    public async Task InvokeGetFilename_ReturnsFilename()
    {
        var script = """
            public record TestNote(string Title);

            var Schema = typeof(TestNote);

            var Prompt = "Analyse.";

            string GetFilename(TestNote note) => note.Title.Replace(":", " -").Replace("/", "_");

            string Format(TestNote note) => note.Title;

            """;
        var path = CreateScriptFile(script);

        try
        {
            var result = await _loader.LoadAsync(path);
            var record = new Dictionary<string, object> { ["Title"] = "2025-06-05 Auto: Werkstatt" };
            var instance = DeserializeToType(record, result.Schema);
            var filename = result.InvokeGetFilename(instance);
            Assert.Equal("2025-06-05 Auto - Werkstatt", filename);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MarkdownFileSupported()
    {
        var script = $$"""
                     # This is our record
                     this holds data
                     ```cs
                     public record TestNote(string Title);
                     var Schema = typeof(TestNote);

                     var Prompt = "Analyse.";
                     ```
                     # This is our GetFilename function
                     ```cs

                     string GetFilename(TestNote note)
                     {
                         return note.Title + "_FOO";
                     }

                     string Format(TestNote note) => note.Title;
                     ```

                     """;
        var path = CreateScriptFile(script,"md");

        try
        {
            var result = await _loader.LoadAsync(path);
            var record = new Dictionary<string, object> { ["Title"] = "2025-06-05 Car" };
            var instance = DeserializeToType(record, result.Schema);
            var filename = result.InvokeGetFilename(instance);
            Assert.Equal("2025-06-05 Car_FOO", filename);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_MarkdownCompileError_ReportsOriginalMarkdownLine()
    {
        var script = """
            # Intro
            ```cs
            public record TestNote(string Title);
            var Schema = typeof(TestNote)
            var Prompt = "Analyse.";
            string GetFilename(TestNote note) => note.Title;
            string Format(TestNote note) => note.Title;
            ```
            """;
        var path = CreateScriptFile(script, "md");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidPipelineScriptException>(() => _loader.LoadAsync(path));
            Assert.Contains("line 4", ex.Message);
            Assert.Contains("var Schema = typeof(TestNote)", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvokeGetFilename_WhenScriptThrows_ReportsLineAndSource()
    {
        var script = """
            public record TestNote(string Title);

            var Schema = typeof(TestNote);
            var Prompt = "Analyse.";

            string GetFilename(TestNote note)
            {
                throw new InvalidOperationException("boom");
            }

            string Format(TestNote note) => note.Title;
            """;
        var path = CreateScriptFile(script);

        try
        {
            var result = await _loader.LoadAsync(path);
            var record = new Dictionary<string, object> { ["Title"] = "x" };
            var instance = DeserializeToType(record, result.Schema);

            var ex = Assert.Throws<InvalidPipelineScriptException>(() => result.InvokeGetFilename(instance));
            Assert.Contains("execution error in GetFilename", ex.Message);
            Assert.Contains("boom", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvokeFormat_ReturnsFormattedMarkdown()
    {
        var script = """
            public record TestNote(string Title, string MarkdownBody);

            var Schema = typeof(TestNote);

            var Prompt = "Analyse.";

            string GetFilename(TestNote note) => note.Title;

            string Format(TestNote note) => $"---\ntitle: {note.Title}\n---\n{note.MarkdownBody}";

            """;
        var path = CreateScriptFile(script);

        try
        {
            var result = await _loader.LoadAsync(path);
            var record = new Dictionary<string, object>
            {
                ["Title"] = "2025-06-05 Auto Werkstatt",
                ["MarkdownBody"] = "Some content"
            };
            var instance = DeserializeToType(record, result.Schema);
            var formatted = result.InvokeFormat(instance);
            Assert.Contains("title: 2025-06-05 Auto Werkstatt", formatted);
            Assert.Contains("Some content", formatted);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadAsync_WithGlobals_InjectsCaptionsAndCurrentDate()
    {
        var script = """
            public record TestNote(string Title);

            var Schema = typeof(TestNote);

            var Prompt = $"Analyse. Date: {CurrentDate:yyyy-MM-dd} Captions: {string.Join(" | ", Captions)}";

            string GetFilename(TestNote note) => note.Title;

            string Format(TestNote note) => note.Title;

            """;
        var path = CreateScriptFile(script);

        try
        {
            var globals = new ScriptGlobals(["caption1", "caption2"], new DateTime(2025, 6, 5));
            var result = await _loader.LoadAsync(path, globals);
            Assert.Contains("2025-06-05", result.Prompt);
            Assert.Contains("caption1 | caption2", result.Prompt);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task InvokeGetFilename_UsingMarkdownHelper()
    {
        var script = """
            using Paperoni.Ai;

            public record TestNote(string Title);

            var Schema = typeof(TestNote);

            var Prompt = "Analyse.";

            string GetFilename(TestNote note)
            {
                var safe = MarkdownHelper.AutoFixDate(note.Title);
                return MarkdownHelper.SanitizeFilename(safe);
            }

            string Format(TestNote note) => note.Title;

            """;
        var path = CreateScriptFile(script);

        try
        {
            var result = await _loader.LoadAsync(path);
            var record = new Dictionary<string, object> { ["Title"] = "2025-06-05 Auto: Werkstatt" };
            var instance = DeserializeToType(record, result.Schema);
            var filename = result.InvokeGetFilename(instance);
            Assert.Equal("2025-06-05 Auto_ Werkstatt", filename);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static object DeserializeToType(Dictionary<string, object> data, Type type)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(data);
        return System.Text.Json.JsonSerializer.Deserialize(json, type)!;
    }
}
