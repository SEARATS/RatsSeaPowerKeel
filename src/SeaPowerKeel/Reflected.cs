using System;
using System.Reflection;
using System.Runtime.ExceptionServices;
using HarmonyLib;

namespace SeaPowerKeel;

/// <summary>Calls through reflection, the way Keel makes them everywhere: into mods and into the game, which Keel references by name only.</summary>
internal static class Reflected
{
    /// <summary>The exception a reflected call really threw (Invoke wraps it in a TargetInvocationException).</summary>
    public static Exception Unwrap(Exception ex) => ex is TargetInvocationException { InnerException: not null } t ? t.InnerException : ex;

    /// <summary>Calls the method; an exception it throws comes out as itself, with its own stack, not wrapped.</summary>
    public static object Invoke(MethodBase method, object target, object[] args)
    {
        try
        {
            return method.Invoke(target, args);
        }
        catch (TargetInvocationException t) when (t.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(t.InnerException).Throw();
            throw;
        }
    }

    /// <summary>The game's <c>Singleton&lt;T&gt;.Instance</c> for its type <paramref name="typeName"/>, or null.</summary>
    public static object Singleton(string typeName)
    {
        Type type = AccessTools.TypeByName(typeName);
        Type singleton = type == null ? null : AccessTools.TypeByName("SeaPower.Singleton`1")?.MakeGenericType(type);
        return singleton?.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null);
    }
}
