using Paperoni.Ai;

namespace Paperoni.Tests;

public class ScriptLoaderTests
{
    private readonly ScriptLoader _loader = new();

    private static string CreateScriptFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"test-script-{Guid.NewGuid()}.csx");
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

            Func<TestNote, string> GetFilename = note => note.Title.Replace(":", " -");

            Func<TestNote, string> Format = note => $"---\ntitle: {note.Title}\n---\n{note.MarkdownBody}";

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
            Assert.Contains("Schema", ex.Message);
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

            Func<TestNote, string> GetFilename = note => note.Title;

            Func<TestNote, string> Format = note => note.Title;
            """;
        var path = CreateScriptFile(script);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidPipelineScriptException>(
                () => _loader.LoadAsync(path));
            Assert.Contains("Prompt", ex.Message);
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

            Func<TestNote, string> Format = note => note.Title;
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
    public async Task LoadAsync_MissingFormat_ThrowsInvalidPipelineScriptException()
    {
        var script = """
            public record TestNote(string Title);

            var Schema = typeof(TestNote);

            var Prompt = "Analyse.";

            Func<TestNote, string> GetFilename = note => note.Title;
            """;
        var path = CreateScriptFile(script);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidPipelineScriptException>(
                () => _loader.LoadAsync(path));
            Assert.Contains("Format", ex.Message);
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

            Func<TestNote, string> GetFilename = note => note.Title.Replace(":", " -").Replace("/", "_");

            Func<TestNote, string> Format = note => note.Title;

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
    public async Task InvokeFormat_ReturnsFormattedMarkdown()
    {
        var script = """
            public record TestNote(string Title, string MarkdownBody);

            var Schema = typeof(TestNote);

            var Prompt = "Analyse.";

            Func<TestNote, string> GetFilename = note => note.Title;

            Func<TestNote, string> Format = note => $"---\ntitle: {note.Title}\n---\n{note.MarkdownBody}";

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

            Func<TestNote, string> GetFilename = note => note.Title;

            Func<TestNote, string> Format = note => note.Title;

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

            Func<TestNote, string> GetFilename = note =>
            {
                var safe = MarkdownHelper.AutoFixDate(note.Title);
                return MarkdownHelper.SanitizeFilename(safe);
            };

            Func<TestNote, string> Format = note => note.Title;

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