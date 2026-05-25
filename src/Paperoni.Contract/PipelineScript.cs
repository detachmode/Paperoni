using System.Diagnostics;
using System.Reflection;

public record ScriptGlobals(List<string> Captions, DateTime CurrentDate);

public sealed class PipelineScript
{
    public Type Schema { get; init; } = null!;
    public string Prompt { get; init; } = null!;
    public Delegate GetFilenameDelegate { get; init; } = null!;
    public Delegate FormatDelegate { get; init; } = null!;
    public ScriptGlobals ScriptGlobals { get; init; } = null!;
    public string ScriptPath { get; init; } = "";
    public IReadOnlyList<string> SourceLines { get; init; } = [];
    public Func<int, int> MapLineNumber { get; init; } = static line => line;

    public string InvokeGetFilename(object record)
    {
        return InvokeStringFunction(GetFilenameDelegate, record, "GetFilename");
    }

    public string InvokeFormat(object record)
    {
        return InvokeStringFunction(FormatDelegate, record, "Format");
    }

    private string InvokeStringFunction(Delegate function, object record, string functionName)
    {
        try
        {
            return (string?)function.DynamicInvoke(record)
                   ?? throw new InvalidOperationException($"{functionName} returned null");
        }
        catch (Exception ex)
        {
            var inner = ex is TargetInvocationException tie && tie.InnerException is not null ? tie.InnerException : ex;
            throw new InvalidPipelineScriptException(BuildExecutionErrorMessage(functionName, inner), inner);
        }
    }

    private string BuildExecutionErrorMessage(string functionName, Exception ex)
    {
        var lineInfo = GetScriptLineInfo(ex);
        if (lineInfo is null)
        {
            return $"Pipeline script execution error in {functionName}: {ex.Message}";
        }

        var (line, sourceLine) = lineInfo.Value;
        return $"Pipeline script execution error in {functionName} at line {line}:{Environment.NewLine}> {sourceLine}{Environment.NewLine}{ex.Message}";
    }

    private (int line, string sourceLine)? GetScriptLineInfo(Exception ex)
    {
        var frames = new StackTrace(ex, true).GetFrames();
        if (frames is null)
        {
            return null;
        }

        foreach (var frame in frames)
        {
            var fileName = frame.GetFileName();
            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            if (!string.Equals(Path.GetFullPath(fileName), Path.GetFullPath(ScriptPath), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var scriptLine = frame.GetFileLineNumber();
            if (scriptLine <= 0)
            {
                continue;
            }

            var sourceLineNumber = MapLineNumber(scriptLine);
            if (sourceLineNumber <= 0 || sourceLineNumber > SourceLines.Count)
            {
                return (sourceLineNumber, "<source line unavailable>");
            }

            return (sourceLineNumber, SourceLines[sourceLineNumber - 1]);
        }

        return null;
    }
}

public class InvalidPipelineScriptException : Exception
{
    public InvalidPipelineScriptException(string message) : base(message) { }
    public InvalidPipelineScriptException(string message, Exception inner) : base(message, inner) { }
}
