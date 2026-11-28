using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace SeaPowerKeel;

// Shared by the stub and the loader. The stub opens suffix-based helpers.
internal static class PreparationEntry
{
    public static Action<string, string[], Action<string>> Open(string dll)
    {
        string typeName;
        using (var metadata = AssemblyDefinition.ReadAssembly(dll))
        {
            ModuleDefinition module = metadata.MainModule;
            string bad = module.AssemblyReferences.Select(r => r.Name).FirstOrDefault(Forbidden);
            if (bad != null)
                throw new InvalidOperationException($"The helper references {bad}. Only use code that can run without the game or loader.");
            string[] types = module.Types.Where(t => t.IsPublic && !t.HasGenericParameters && t.Methods.Any(IsPrepare))
                .Select(t => t.FullName).Take(2).ToArray();
            if (types.Length != 1)
                throw new InvalidOperationException("Expected one public static void Prepare(string, string[], Action<string>).");
            typeName = types[0];
        }
        Assembly assembly = Assembly.LoadFrom(dll);
        if (!ModFolders.SamePath(Path.GetFullPath(assembly.Location), dll))
            throw new InvalidOperationException($"The assembly name is already used by {assembly.Location}.");
        Type[] signature = [typeof(string), typeof(string[]), typeof(Action<string>)];
        MethodInfo method = assembly.GetType(typeName, throwOnError: true)
            .GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Single(m => m.Name == "Prepare" && !m.IsGenericMethod && m.ReturnType == typeof(void)
                && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(signature));
        return (Action<string, string[], Action<string>>)method.CreateDelegate(typeof(Action<string, string[], Action<string>>));
    }

    private static bool IsPrepare(MethodDefinition m) =>
        m.Name == "Prepare" && m.IsPublic && m.IsStatic && !m.HasGenericParameters && m.ReturnType.FullName == "System.Void"
        && m.Parameters.Select(p => p.ParameterType.FullName)
            .SequenceEqual(new[] { "System.String", "System.String[]", "System.Action`1<System.String>" });

    internal static bool Forbidden(string reference) =>
        reference.StartsWith("Unity", StringComparison.OrdinalIgnoreCase) || reference.StartsWith("AnchorChain", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("SeaPowerKeel", StringComparison.OrdinalIgnoreCase)
        || reference is "Assembly-CSharp" or "Seapower-Scripts" or "Noesis.NoesisGUI" or "BepInEx" or "0Harmony";
}
