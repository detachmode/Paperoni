namespace Paperoni.Contract;

public record PipelineResult(string Filename, Dictionary<string, object> Record);