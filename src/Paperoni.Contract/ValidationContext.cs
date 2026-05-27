namespace Paperoni.Contract;

public sealed class ValidationContext
{
    private readonly List<string> _failures = [];

    public IReadOnlyList<string> Failures => _failures;
    public bool HasFailures => _failures.Count > 0;

    public void Assert(bool condition, string message)
    {
        if (!condition)
        {
            _failures.Add(message);
        }
    }
}
