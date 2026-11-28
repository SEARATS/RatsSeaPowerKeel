using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace SeaPowerKeel;

internal enum StartupHelper { Prepare, Preload }

// Shared by the stub and loader. Each entry point is selected explicitly.
internal static class PreparationEntry
{
    public static Action<string, string[], Action<string>> Open(string dll, StartupHelper helper)
    {
        string entry = helper.ToString();
        string typeName;
        using (var metadata = AssemblyDefinition.ReadAssembly(dll))
        {
            ModuleDefinition module = metadata.MainModule;
            string bad = module.AssemblyReferences.Select(r => r.Name).FirstOrDefault(Forbidden);
            if (bad != null)
                throw new InvalidOperationException($"The helper references {bad}. Only use code that can run without the game or loader.");
            string[] types = module.Types.Where(t => t.IsPublic && !t.HasGenericParameters && t.Methods.Any(m => IsEntry(m, entry)))
                .Select(t => t.FullName).Take(2).ToArray();
            if (types.Length != 1)
                throw new InvalidOperationException($"Expected one public static void {entry}(string, string[], Action<string>).");
            typeName = types[0];
        }
        Assembly assembly = Assembly.LoadFrom(dll);
        if (!ModFolders.SamePath(Path.GetFullPath(assembly.Location), dll))
            throw new InvalidOperationException($"The assembly name is already used by {assembly.Location}.");
        Type[] signature = [typeof(string), typeof(string[]), typeof(Action<string>)];
        MethodInfo method = assembly.GetType(typeName, throwOnError: true)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Single(m => m.Name == entry && !m.IsGenericMethod && m.ReturnType == typeof(void)
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(signature));
        return (Action<string, string[], Action<string>>)method.CreateDelegate(typeof(Action<string, string[], Action<string>>));
    }

    private static bool IsEntry(MethodDefinition m, string entry) =>
        m.Name == entry && m.IsPublic && m.IsStatic && !m.HasGenericParameters && m.ReturnType.FullName == "System.Void"
        && m.Parameters.Select(p => p.ParameterType.FullName)
            .SequenceEqual(new[] { "System.String", "System.String[]", "System.Action`1<System.String>" });

    internal static bool Forbidden(string reference) =>
        reference.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) || reference.StartsWith("AnchorChain", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("SeaPowerKeel", StringComparison.OrdinalIgnoreCase)
        || reference is "Assembly-CSharp" or "Seapower-Scripts" or "Noesis.NoesisGUI" or "BepInEx" or "0Harmony";
}
