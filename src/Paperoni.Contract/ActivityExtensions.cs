using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Paperoni.Contract;

public static class ActivityExtensions
{
    public static Activity? StartActivity<T>(this ActivitySource source, [CallerMemberName] string? methodName = null)
    {
        return source.StartActivity($"{typeof(T).Name}.{methodName}");
    }
}
