using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using TypeAttributes = Mono.Cecil.TypeAttributes;
using MethodAttributes = Mono.Cecil.MethodAttributes;

namespace SeaPowerKeel;

/// <summary>
/// What a mod that touches Anchor Chain's <c>AnchorChainLoader</c> class needs when Keel, not Anchor Chain, runs the mods.
/// Euromod's Sonar Audio Loader (CustomAudioLoader.dll) calls <c>AnchorChainLoader.FindFile</c> to find audio and mapping
/// files that live in OTHER mods' folders. Two things are then missing:
/// - The class implements <c>AnchorChain.Preloader.IPluginLoader</c>, which lives in <c>AnchorChain.Preloader.dll</c>, a
/// BepInEx plugin. Without that DLL (Keel's setup removes it) the class cannot be loaded at all: a <c>TypeLoadException</c> at
/// the first call. <see cref="BuildPreloaderStub"/> makes an assembly of that name with just that interface.
/// - <c>FindFile</c> searches the mod folders and Anchor Chain's config folder. <see cref="Prime"/> sets them up the way
/// Anchor Chain's own loader does, for both shapes of the class:
///   - 1.1.0 (lib\AnchorChain.keel.bin, the copy Keel ships): a private static <c>_allDirectories</c> list that only its
///     <c>Setup</c> fills, plus <c>_configPath</c>;
///   - 1.2.0 (the source under anchor-chain\, and a subscribed copy of that version): no list, <c>FindFile</c> reads the game's
///     own mod folders and then <c>_configPath</c>, so only <c>_configPath</c> needs setting.
/// </summary>
internal static class AcLoaderShim
{
    public const string PreloaderAssembly = "AnchorChain.Preloader";
    public const string LoaderType = "AnchorChain.AnchorChainLoader";

    /// <summary>An assembly named AnchorChain.Preloader (1.0.0.0, what Anchor Chain 1.1.0 references) with <c>AnchorChain.Preloader.IPluginLoader { void LoadPlugins(); }</c>.</summary>
    public static Assembly BuildPreloaderStub()
    {
        AssemblyDefinition asm = AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition(PreloaderAssembly, new Version(1, 0, 0, 0)), PreloaderAssembly, ModuleKind.Dll);
        ModuleDefinition module = asm.MainModule;
        var iface = new TypeDefinition("AnchorChain.Preloader", "IPluginLoader",
                                       TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract | TypeAttributes.AutoClass | TypeAttributes.AnsiClass);
        iface.Methods.Add(new MethodDefinition("LoadPlugins",
                                               MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Abstract | MethodAttributes.Virtual,
                                               module.TypeSystem.Void));
        module.Types.Add(iface);
        using var stream = new MemoryStream();
        asm.Write(stream);
        return Assembly.Load(stream.ToArray());
    }

    private static string Norm(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

    private const BindingFlags Statics = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    /// <summary>
    /// Sets up what <c>AnchorChainLoader.FindFile</c> searches (see the class comment for the two shapes). Returns null when done,
    /// else why it could not (the mods that call <c>FindFile</c> then find nothing).
    /// </summary>
    public static string Prime(Assembly anchorChain, IEnumerable<string> folders, string configDir)
    {
        Type loader = anchorChain.GetType(LoaderType, throwOnError: false);
        if (loader == null)
            return $"{LoaderType} is not in {anchorChain.GetName().Name} {anchorChain.GetName().Version}";
        if (loader.GetMethod("FindFile", Statics, null, new[] { typeof(string) }, null) == null)
            return $"AnchorChainLoader has no FindFile(string) in {anchorChain.GetName().Name} {anchorChain.GetName().Version}";
        FieldInfo config = loader.GetField("_configPath", Statics);
        if (config == null || config.FieldType != typeof(string))
            return $"AnchorChainLoader has no _configPath in {anchorChain.GetName().Name} {anchorChain.GetName().Version}";
        FieldInfo list = loader.GetField("_allDirectories", Statics);
        if (list != null)
        {
            if (list.GetValue(null) is not HashSet<DirectoryInfo> set)
                return $"AnchorChainLoader._allDirectories is a {list.FieldType.Name}, not the folder set of Anchor Chain 1.1.0";
            foreach (string folder in folders.Where(f => !string.IsNullOrEmpty(f)))
                AddOnce(set, folder);
            if (!string.IsNullOrEmpty(configDir) && Directory.Exists(configDir))
                AddOnce(set, configDir);
        }
        if (!string.IsNullOrEmpty(configDir))
            config.SetValue(null, configDir);
        return null;
    }

    private static void AddOnce(HashSet<DirectoryInfo> set, string folder)
    {
        if (!set.Any(d => string.Equals(Norm(d.FullName), Norm(folder), StringComparison.OrdinalIgnoreCase)))
            set.Add(new DirectoryInfo(folder));
    }
}
