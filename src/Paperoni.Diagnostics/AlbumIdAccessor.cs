namespace Paperoni.Diagnostics;

public sealed class AlbumIdAccessor
{
    private static readonly AsyncLocal<int?> s_current = new();

    public int? Id
    {
        get => s_current.Value;
        set => s_current.Value = value;
    }

    internal static int? GetCurrentId() => s_current.Value;
}
