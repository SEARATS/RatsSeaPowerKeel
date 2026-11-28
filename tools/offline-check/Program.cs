using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SeaPowerKeel;

// SeaPowerKeel.OfflineCheck <Managed folder of a game build> <BepInEx core folder> <mod folder or folder of mod folders>...
// Scans the given mod folders the way Keel does in the game, checks every plugin DLL against the given game build and prints
// the plan and the report Keel would show (nothing is loaded or started). The game build is told by the Managed folder: the
// game type Keel uses to tell the maps apart is looked up there.
internal static class Program
{
    private static int Main(string[] args)
    {
        if (args.Length == 4 && args[0] == "--snapshot-methods")
        {
            Version version = InfoVersion.Number(args[1]) ?? throw new ArgumentException("Expected a Sea Power build version.");
            var snapshot = MethodSnapshot.Scan(args[1], File.ReadAllBytes(Path.Combine(args[2], MethodSnapshot.AssemblyFile)));
            string path = Path.Combine(args[3], version.ToString(3), snapshot.AssemblyHash + MethodSnapshot.Extension);
            snapshot.Write(path);
            Console.WriteLine($"Wrote {snapshot.Methods.Count:N0} methods for Sea Power {snapshot.Build} to {path}");
            return 0;
        }
        if (args.Length == 5 && args[0] == "--preloader-compat")
            return SelfTest.CheckReleasedPreloaders(args[1], args[2], args[3], args[4]);
        if (args.Length > 0 && args[0] == "--selftest")
            return SelfTest.Run(args.Skip(1).ToArray());
        if (args.Length > 0 && args[0] == "--inicheck")
            return SelfTest.IniCheck(args.Skip(1).ToArray());
        if (args.Length < 3)
        {
            Console.WriteLine("usage: SeaPowerKeel.OfflineCheck <Managed> <BepInEx core> <mod folder | folder of mod folders> ...");
            Console.WriteLine("       SeaPowerKeel.OfflineCheck --selftest <Managed> <BepInEx core>      (Keel's own tests; exit code 1 when one fails)");
            Console.WriteLine("       SeaPowerKeel.OfflineCheck --inicheck <Managed> [<Managed> ...]     (do the game's IniHandler members [ACConfig] needs exist?)");
            Console.WriteLine("       SeaPowerKeel.OfflineCheck --preloader-compat <Managed> <previous Keel stub DLL> <Anchor Chain preloader DLL> <Anchor Chain loader DLL>");
            Console.WriteLine("       SeaPowerKeel.OfflineCheck --snapshot-methods <game build version> <Managed> <history directory>");
            return 2;
        }
        string managed = args[0], core = args[1];
        Program.ResolveFrom(managed);
        Console.WriteLine($"Game build: {GameState.BuildName} ({managed})");

        var folders = new List<ModFolder>();
        foreach (string root in args.Skip(2))
        {
            bool isMod = Directory.GetFiles(root, "*.dll", SearchOption.TopDirectoryOnly).Any() || File.Exists(Path.Combine(root, "_info.ini"));
            IEnumerable<string> dirs = isMod ? new[] { root } : Directory.GetDirectories(root);
            foreach (string d in dirs)
            {
                string name = Path.GetFileName(d);
                folders.Add(new ModFolder { Path = d, Name = name, SteamId = ulong.TryParse(name, out ulong id) ? id : 0, Enabled = true, Order = folders.Count });
            }
        }

        var notes = new List<string>();
        var skipped = new List<SkippedDll>();
        List<PluginEntry> all = Scanner.Scan(folders, notes.Add, skipped, managed, core);
        using (var pre = new Preflight(managed, core))
        {
            // The game's own verdict (GameVersionCheck = Standard). The game's version is not known here, so _info.ini statements are not judged.
            List<IGrouping<string, PluginEntry>> dlls = all.Where(p => p.State == PluginState.Pending).GroupBy(p => p.Dll).ToList();
            var results = dlls.ToDictionary(d => d.Key, d => pre.Check(d.Key), StringComparer.OrdinalIgnoreCase);
            foreach (IGrouping<string, PluginEntry> dll in dlls)
            {
                string matching = Preflight.MatchingMapBuild(dll.Key, results[dll.Key], results);
                foreach (PluginEntry p in dll)
                    Preflight.Judge(p, results[dll.Key], matching, strict: false, declared: null);
            }
            Console.WriteLine($"Type index for by-name lookups: {(pre.IndexMs > 0 ? pre.IndexMs + " ms" : "not needed")}");
        }
        List<PluginEntry> order = Planner.Plan(all, new HashSet<string>(), new HashSet<string>(), "Sea Power");
        foreach (PluginEntry p in order)
            p.State = PluginState.Running;   // would start; nothing is run here
        Planner.UpdateLoadOrderWarnings(all, folders);
        Console.WriteLine("Start order: " + string.Join(" > ", order.Select(p => p.Display)));
        Console.WriteLine();
        Console.WriteLine(Report.Text(all, "Offline check (nothing started; RUNNING = would start)", notes, skipped));
        return 0;
    }

    /// <summary>
    /// Assemblies the runtime cannot find come from that Managed folder: Type.GetType then finds the game's Seapower-Scripts there,
    /// which is how Keel tells the builds apart.
    /// </summary>
    internal static void ResolveFrom(string managed) => AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
    {
        string p = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
        return File.Exists(p) ? Assembly.LoadFrom(p) : null;
    };
}
