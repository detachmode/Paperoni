namespace Paperoni.Ai;

public record ScriptGlobals(List<string> Captions, DateTime CurrentDate);

public sealed class PipelineScript
{
    public Type Schema { get; init; } = null!;
    public string Prompt { get; init; } = null!;
    public Delegate GetFilenameDelegate { get; init; } = null!;
    public Delegate FormatDelegate { get; init; } = null!;
    public ScriptGlobals ScriptGlobals { get; init; } = null!;

    public string InvokeGetFilename(object record)
    {
        return (string?)GetFilenameDelegate.DynamicInvoke(record)
               ?? throw new InvalidOperationException("GetFilename returned null");
    }

    public string InvokeFormat(object record)
    {
        return (string?)FormatDelegate.DynamicInvoke(record)
               ?? throw new InvalidOperationException("Format returned null");
    }
}

public class InvalidPipelineScriptException : Exception
{
    public InvalidPipelineScriptException(string message) : base(message) { }
    public InvalidPipelineScriptException(string message, Exception inner) : base(message, inner) { }
}