using System.Diagnostics;

namespace Paperoni.Diagnostics;

public sealed class ActivityScope
{
    private readonly Activity? _activity;

    internal ActivityScope(Activity? activity)
    {
        _activity = activity;
        if (AlbumIdAccessor.GetCurrentId() is { } id)
        {
            _activity?.SetTag("AlbumId", id);
        }
    }

    public ActivityScope SetTag(string key, object? value)
    {
        _activity?.SetTag(key, value);
        return this;
    }

    public void SetStatus(ActivityStatusCode status, string? description = null)
    {
        _activity?.SetStatus(status, description);
    }

    internal void SetOk() => _activity?.SetStatus(ActivityStatusCode.Ok);

    internal void SetError(Exception exception) => _activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
}
