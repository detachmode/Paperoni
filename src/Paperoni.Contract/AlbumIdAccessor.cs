namespace Paperoni.Contract;

public sealed class AlbumIdAccessor
{
    private static readonly AsyncLocal<int?> _current = new();
    public int? Id { get => _current.Value; set => _current.Value = value; }
}
