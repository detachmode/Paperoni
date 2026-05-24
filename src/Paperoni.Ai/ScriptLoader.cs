using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace Paperoni.Ai;

public interface IScriptLoader
{
    Task<PipelineScript> LoadAsync(string scriptPath, ScriptGlobals? globals = null);
}

public class ScriptLoader : IScriptLoader
{
    private static readonly string[] s_requiredConventions = ["Schema", "Prompt", "GetFilename", "Format"];

    public async Task<PipelineScript> LoadAsync(string scriptPath, ScriptGlobals? globals = null)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException("Pipeline script not found", scriptPath);
        }

        var scriptContent = await File.ReadAllTextAsync(scriptPath);

        var assemblyPaths = AppDomain.CurrentDomain.GetAssemblies()
#pragma warning disable IL3000
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => a.Location)
#pragma warning restore IL3000
            .Distinct()
            .ToArray();

        var scriptOptions = ScriptOptions.Default
            .WithImports("System", "System.Collections.Generic", "System.Linq", "System.ComponentModel")
            .WithReferences(assemblyPaths)
            .WithReferences(typeof(MarkdownHelper).Assembly);

        ScriptState<object> scriptState;
        try
        {
            scriptState = await CSharpScript.RunAsync(scriptContent, scriptOptions, globals);
        }
        catch (CompilationErrorException ex)
        {
            var errors = string.Join(Environment.NewLine, ex.Diagnostics.Select(d => d.ToString()));
            throw new InvalidPipelineScriptException($"Pipeline script compile error:{Environment.NewLine}{errors}", ex);
        }

        var missing = s_requiredConventions
            .Where(name => GetScriptVariable(scriptState, name) is null)
            .ToList();

        if (missing.Count > 0)
        {
            throw new InvalidPipelineScriptException(
                $"Pipeline script is missing required conventions: {string.Join(", ", missing)}");
        }

        var schema = (Type)GetScriptVariable(scriptState, "Schema")!;
        var prompt = (string)GetScriptVariable(scriptState, "Prompt")!;
        var getFilenameDelegate = (Delegate)GetScriptVariable(scriptState, "GetFilename")!;
        var formatDelegate = (Delegate)GetScriptVariable(scriptState, "Format")!;

        return new PipelineScript
        {
            Schema = schema,
            Prompt = prompt,
            GetFilenameDelegate = getFilenameDelegate,
            FormatDelegate = formatDelegate,
            ScriptGlobals = globals ?? new ScriptGlobals([], DateTime.Now)
        };
    }

    private static object? GetScriptVariable(ScriptState<object> state, string name)
    {
        return state.Variables.FirstOrDefault(v => v.Name == name)?.Value;
    }
}