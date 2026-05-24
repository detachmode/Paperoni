using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Paperoni.Ai;

namespace Paperoni.AlbumProcessing;

public interface IScriptLoader
{
    Task<PipelineScript> LoadAsync(string scriptPath, ScriptGlobals? globals = null);
}

public class ScriptLoader : IScriptLoader
{
    public async Task<PipelineScript> LoadAsync(string scriptPath, ScriptGlobals? globals = null)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Pipeline script not found", scriptPath);
        }

        var scriptContent = await File.ReadAllTextAsync(scriptPath);

        if (Path.GetExtension(scriptPath).Equals(".md", StringComparison.OrdinalIgnoreCase))
        {
            scriptContent = ExtractScriptFromMarkdown(scriptContent);
        }

        ScriptState<object> scriptState;
        try
        {
            scriptState = await CSharpScript.RunAsync(scriptContent, ScriptOptions, globals);
        }
        catch (CompilationErrorException ex)
        {
            var errors = string.Join(Environment.NewLine, ex.Diagnostics.Select(d => d.ToString()));
            throw new InvalidPipelineScriptException($"Pipeline script compile error:{Environment.NewLine}{errors}",
                ex);
        }

        var schema = (Type)GetScriptVariable(scriptState, "Schema")!;
        FailIfNull(schema, "Schema variable must be set");

        var prompt = (string)GetScriptVariable(scriptState, "Prompt")!;
        FailIfNull(prompt, "Prompt variable must be set");

        var getFileNameFunc = (await scriptState.ContinueWithAsync<Delegate>("GetFilename")).ReturnValue;
        var formatFunc = (await scriptState.ContinueWithAsync<Delegate>("Format")).ReturnValue;

        return new PipelineScript
        {
            Schema = schema,
            Prompt = prompt,
            GetFilenameDelegate = getFileNameFunc,
            FormatDelegate = formatFunc,
            ScriptGlobals = globals ?? new ScriptGlobals([], DateTime.Now)
        };
    }

    private static string ExtractScriptFromMarkdown(string scriptContent)
    {
        var lines = scriptContent.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries).ToList();
        var script = GetScriptLines();
        var joined =  string.Join(Environment.NewLine, script);
        return joined;

        IEnumerable<string> GetScriptLines()
        {
            while (lines.Count > 0)
            {

                if (!lines[0].StartsWith("```cs"))
                {
                    lines.RemoveAt(0);
                    continue;
                }

                lines.RemoveAt(0);

                while (!lines[0].StartsWith("```"))
                {
                    yield return lines[0];
                    lines.RemoveAt(0);
                }
                lines.RemoveAt(0);

            }

        }
    }

    private static ScriptOptions ScriptOptions
    {
        get
        {
            if(field != null)
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
                .WithImports("System", "System.Collections.Generic", "System.Linq", "System.ComponentModel")
                .WithReferences(assemblyPaths);
            field = scriptOptions;

            return field;
        }
    }

    public static void FailIfNull(object o, string message)
    {
        if (o == null)
        {
            throw new InvalidPipelineScriptException("Invalid Pipeline script: "+message);
        }
    }

    private static object? GetScriptVariable(ScriptState<object> state, string name)
    {
        return state.Variables.FirstOrDefault(v => v.Name == name)?.Value;
    }
}
