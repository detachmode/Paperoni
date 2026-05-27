using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.Logging;

namespace Paperoni.Ai;

public interface IScriptLoader
{
    Task<PipelineScript> LoadAsync(string scriptPath, ScriptGlobals? globals = null);
}

public class ScriptLoader(ILoggerFactory loggerFactory) : IScriptLoader
{
    public async Task<PipelineScript> LoadAsync(string scriptPath, ScriptGlobals? globals = null)
    {
        globals ??= new ScriptGlobals([], DateTime.Now);
        var scriptLogger = loggerFactory.CreateLogger("PipelineScript.Local");
        globals.Logger = scriptLogger;
        globals.Log = LogFromScript;

        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Pipeline script not found", scriptPath);
        }

        var originalContent = await File.ReadAllTextAsync(scriptPath);
        var originalLines = SplitLines(originalContent);
        var scriptContent = originalContent;
        var scriptLineToSourceLine = Enumerable.Range(1, originalLines.Count).ToArray();

        if (Path.GetExtension(scriptPath).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            var extraction = ExtractScriptFromMarkdown(originalContent);
            scriptContent = extraction.ScriptContent;
            scriptLineToSourceLine = extraction.ScriptLineToSourceLine;
        }

        ScriptState<object> scriptState;
        try
        {
            scriptState = await CSharpScript.RunAsync(scriptContent, ScriptOptions.WithFilePath(scriptPath), globals);
        }
        catch (CompilationErrorException ex)
        {
            var errors = FormatDiagnostics(ex.Diagnostics, originalLines, scriptLineToSourceLine);
            throw new InvalidPipelineScriptException($"Pipeline script compile error:{Environment.NewLine}{errors}",
                ex);
        }

        var schema = (Type)GetScriptVariable(scriptState, "Schema")!;
        FailIfNull(schema, "Schema variable must be set");

        var prompt = (string)GetScriptVariable(scriptState, "Prompt")!;
        FailIfNull(prompt, "Prompt variable must be set");

        Delegate getFileNameFunc;
        Delegate formatFunc;
        try
        {
            getFileNameFunc = (await scriptState.ContinueWithAsync<Delegate>("GetFilename")).ReturnValue;
            formatFunc = (await scriptState.ContinueWithAsync<Delegate>("Format")).ReturnValue;
        }
        catch (CompilationErrorException ex)
        {
            var errors = FormatDiagnostics(ex.Diagnostics, originalLines, scriptLineToSourceLine);
            throw new InvalidPipelineScriptException($"Pipeline script compile error:{Environment.NewLine}{errors}",
                ex);
        }

        Delegate? validateFunc = null;
        try
        {
            validateFunc = (await scriptState.ContinueWithAsync<Delegate>("Validate")).ReturnValue;
        }
        catch (CompilationErrorException ex) when (ex.Diagnostics.Any(d => d.Id == "CS0103"))
        {
            // Validate is optional — only ignore when the symbol is not defined
        }

        return new PipelineScript
        {
            Schema = schema,
            Prompt = prompt,
            GetFilenameDelegate = getFileNameFunc,
            FormatDelegate = formatFunc,
            ValidateDelegate = validateFunc,
            ScriptGlobals = globals,
            ScriptPath = scriptPath,
            SourceLines = originalLines,
            MapLineNumber = line => MapLineNumber(line, scriptLineToSourceLine)
        };

        void LogFromScript(object log)
        {
            var type = log.GetType();
            if (type.IsPrimitive || type == typeof(string))
            {
                scriptLogger.LogInformation(log.ToString());
            }
            else
            {
                var json = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true, });
                scriptLogger.LogInformation(json);
            }
        }
    }

    private static MarkdownExtractionResult ExtractScriptFromMarkdown(string scriptContent)
    {
        var lines = SplitLines(scriptContent);
        var scriptLines = new List<string>();
        var map = new List<int>();

        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].StartsWith("```cs"))
            {
                continue;
            }

            for (var j = i + 1; j < lines.Count; j++)
            {
                if (lines[j].StartsWith("```"))
                {
                    i = j;
                    break;
                }

                scriptLines.Add(lines[j]);
                map.Add(j + 1);
            }
        }

        return new MarkdownExtractionResult(string.Join(Environment.NewLine, scriptLines), map.ToArray());
    }

    private static string FormatDiagnostics(
        IEnumerable<Microsoft.CodeAnalysis.Diagnostic> diagnostics,
        IReadOnlyList<string> sourceLines,
        IReadOnlyList<int> scriptLineToSourceLine)
    {
        return string.Join(Environment.NewLine, diagnostics.Select(d =>
        {
            var span = d.Location.GetLineSpan();
            if (!span.IsValid)
            {
                return d.ToString();
            }

            var scriptLine = span.StartLinePosition.Line + 1;
            var sourceLine = MapLineNumber(scriptLine, scriptLineToSourceLine);
            var col = span.StartLinePosition.Character + 1;
            var lineText = sourceLine > 0 && sourceLine <= sourceLines.Count
                ? sourceLines[sourceLine - 1]
                : "<source line unavailable>";

            return $"{d.Id}: {d.GetMessage()} (line {sourceLine}, col {col}){Environment.NewLine}> {lineText}";
        }));
    }

    private static int MapLineNumber(int scriptLine, IReadOnlyList<int> scriptLineToSourceLine)
    {
        if (scriptLine <= 0 || scriptLine > scriptLineToSourceLine.Count)
        {
            return scriptLine;
        }

        return scriptLineToSourceLine[scriptLine - 1];
    }

    private static List<string> SplitLines(string content)
    {
        return content
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Split('\n')
            .ToList();
    }

    private sealed record MarkdownExtractionResult(string ScriptContent, int[] ScriptLineToSourceLine);

    private static ScriptOptions ScriptOptions
    {
        get
        {
            if (field != null)
            {
                return field;
            }

            var assemblyPaths = AppDomain.CurrentDomain.GetAssemblies()
#pragma warning disable IL3000
                .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                .Select(a => a.Location)
#pragma warning restore IL3000
                .Distinct()
                .ToArray();

            var scriptOptions = ScriptOptions.Default
                .WithImports("System", "System.Collections.Generic", "System.Linq", "System.ComponentModel",
                    "Microsoft.Extensions.Logging")
                .WithReferences(assemblyPaths);
            field = scriptOptions;

            return field;
        }
    }

    public static void FailIfNull(object o, string message)
    {
        if (o == null)
        {
            throw new InvalidPipelineScriptException("Invalid Pipeline script: " + message);
        }
    }

    private static object? GetScriptVariable(ScriptState<object> state, string name)
    {
        return state.Variables.FirstOrDefault(v => v.Name == name)?.Value;
    }
}
