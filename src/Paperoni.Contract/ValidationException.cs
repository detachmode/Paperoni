namespace Paperoni.Contract;

public class ValidationException : Exception
{
    public IReadOnlyList<string> Failures { get; }

    public ValidationException(IReadOnlyList<string> failures)
        : base(string.Join(Environment.NewLine, failures))
    {
        Failures = failures;
    }
}
