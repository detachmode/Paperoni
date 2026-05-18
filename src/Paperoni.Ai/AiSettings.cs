namespace Paperoni.Ai;

public class AiSettings
{
    public required string Model { get; set; }
    public required string Endpoint { get; set; }

    /// <summary>
    /// Optional because local LLM do not need it
    /// </summary>
    public string? ApiKey { get; set; }
}
