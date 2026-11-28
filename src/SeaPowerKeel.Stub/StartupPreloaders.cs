using System;
using System.Reflection;

namespace SeaPowerKeel.Stub;

internal static class StartupPreloaders
{
    internal static bool Boot(Type keel, string version, string path)
    {
        const BindingFlags publicStatic = BindingFlags.Public | BindingFlags.Static;
        if (keel.GetMethod("BootWithPreload", publicStatic) is MethodInfo bootWithPreload)
        {
            bootWithPreload.Invoke(null, new object[] { version, path,
                (Func<string, Action<string, string[], Action<string>>>)Open });
            return true;
        }
        MethodInfo boot = keel.GetMethod("Boot", publicStatic);
        if (boot == null)
            return false;
        boot.Invoke(null, new object[] { version, path });
        return true;
    }

    private static Action<string, string[], Action<string>> Open(string path) =>
        PreparationEntry.Open(path, StartupHelper.Preload);
}
