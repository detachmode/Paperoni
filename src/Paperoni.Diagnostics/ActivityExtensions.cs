using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Paperoni.Diagnostics;

public static class ActivityExtensions
{
    public static readonly ActivitySource Tracer = new("Paperoni");
    
    public static Activity? StartActivity<T>(this ActivitySource source, [CallerMemberName] string? methodName = null)
    {
        return source.StartActivity($"{typeof(T).Name}.{methodName}");
    }

    public static async Task TraceAsync<T>(this ActivitySource source,
        Func<ActivityScope, Task> action,
        [CallerMemberName] string? methodName = null)
    {
        using var activity = source.StartActivity<T>(methodName);
        var scope = new ActivityScope(activity);
        try
        {
            await action(scope);
            scope.SetOk();
        }
        catch (Exception ex)
        {
            scope.SetError(ex);
            throw;
        }
    }

    public static async Task<TResult> TraceAsync<T, TResult>(this ActivitySource source,
        Func<ActivityScope, Task<TResult>> action,
        [CallerMemberName] string? methodName = null)
    {
        using var activity = source.StartActivity<T>(methodName);
        var scope = new ActivityScope(activity);
        try
        {
            var result = await action(scope);
            scope.SetOk();
            return result;
        }
        catch (Exception ex)
        {
            scope.SetError(ex);
            throw;
        }
    }
}
