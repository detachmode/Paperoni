namespace Paperoni.Ai;

public class AiSettings
{
    public required string Model { get; set; }
    public required string Endpoint { get; set; }
    public string? ApiKey { get; set; }
    public int TimeoutSeconds { get; set; } = 600;
    public int MaxRetries { get; set; } = 2;
}
