using System;
using System.Reflection;

namespace Jellyfin.Plugin.Tissue.Tests;

internal class TestDispatchProxy<T> : DispatchProxy
    where T : class
{
    public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = static (_, _) => throw new NotSupportedException();

    public static T Create(Func<MethodInfo, object?[]?, object?> handler)
    {
        var proxy = Create<T, TestDispatchProxy<T>>();
        ((TestDispatchProxy<T>)(object)proxy).Handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        return Handler(targetMethod, args);
    }
}
