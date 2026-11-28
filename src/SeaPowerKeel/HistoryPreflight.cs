using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace SeaPowerKeel;

internal class HistoryPreflight(string managed, string core, string history, string cache, string appVersion, Action<string> log)
{
    private const string ResultFormat = "keel-preflight-v1";
    private readonly Dictionary<string, MethodSnapshot> snapshots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> hashes = new(StringComparer.Ordinal);
    private string environment;
    private MethodSnapshot installed;

    internal class Review(Preflight.Result result, string[] warnings)
    {
        public Preflight.Result Result { get; } = result;
        public string[] Warnings { get; } = warnings;
    }

    public string[] Baselines(string[] compatibility)
    {
        Version running = InfoVersion.Number(appVersion);
        if (running == null || !Directory.Exists(history))
            return [];
        foreach (var candidate in Directory.EnumerateDirectories(history)
            .Select(path => (Path: path, Name: Path.GetFileName(path)))
            .Select(x => (x.Path, x.Name, Number: InfoVersion.Number(x.Name)))
            .Where(x => x.Number != null && x.Number.ToString(3) == x.Name && x.Number < running)
            .Where(x => InfoVersion.Mismatch(compatibility, x.Name) == null)
            .OrderByDescending(x => x.Number))
        {
            string[] files = Directory.GetFiles(candidate.Path, "*" + MethodSnapshot.Extension);
            if (files.Length != 0)
                return files.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        }
        return [];
    }

    // Called on a worker, one DLL at a time. Failures in the optional diagnostics leave the normal verdict in place.
    public Review Check(Preflight preflight, string dll, string[] baselines)
    {
        byte[] image = null;
        string path = null;
        try
        {
            image = File.ReadAllBytes(dll);
            using var sha = SHA256.Create();
            string modHash = FileHash.Hex(sha.ComputeHash(image));
            string input = string.Join("\n", new[] { EnvironmentInputs(), appVersion }.Concat(baselines.Select(HashOf)));
            string inputHash = FileHash.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(input)));
            path = Path.Combine(cache, "results", inputHash, modHash + MethodSnapshot.Extension);
            if (TryRead(path, ReadReview) is Review cached)
            {
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow);   // marks it as in use for Prune
                log($"Method history cache hit for {Path.GetFileName(dll)}.");
                return cached;
            }
        }
        catch (Exception ex)
        {
            log($"Method history cache unavailable: {ex.Message}");
        }

        Preflight.Result result = preflight.Check(dll, image);
        string[] warnings = [];
        try
        {
            if (result.PatchTargets.Count != 0 || result.LookupTargets.Count != 0)
            {
                MethodSnapshot[] previous = baselines.Select(GetSnapshot).ToArray();
                installed ??= InstalledSnapshot();
                warnings = Changes(result, installed, previous);
            }
            if (path != null && !result.PatchCheckIncomplete)
            {
                MethodSnapshot.WriteAtomically(path, w => WriteReview(w, result, warnings));
                Prune();
            }
            log($"Checked method history for {Path.GetFileName(dll)}.");
        }
        catch (Exception ex)
        {
            log($"Could not check method history for {Path.GetFileName(dll)}: {ex.Message}");
        }
        return new Review(result, warnings);
    }

    private string EnvironmentInputs()
    {
        if (environment != null)
            return environment;
        // The ordinary preflight also resolves types outside Seapower-Scripts. Include everything it can resolve.
        // Size and write time, not a SHA-256: hashing the whole Managed folder at every start cost more than the cache saved.
        // The game's own assembly, the one the history compares, keeps its full hash.
        // Steam and BepInEx rewrite the files they update, so a changed DLL still changes its fingerprint.
        IEnumerable<string> Fingerprints(string directory) => Directory.EnumerateFiles(directory, "*.dll")
            .OrderBy(x => x, StringComparer.Ordinal)
            .Select(x => new FileInfo(x))
            .Select(x => x.Name + "=" + (x.Name == MethodSnapshot.AssemblyFile ? HashOf(x.FullName) : x.Length + "@" + x.LastWriteTimeUtc.Ticks));
        return environment = string.Join("\n", new[] { ResultFormat, typeof(Preflight).Module.ModuleVersionId.ToString(), "Managed" }
            .Concat(Fingerprints(managed)).Append("BepInEx").Concat(Fingerprints(core)));
    }

    private string HashOf(string path)
    {
        if (!hashes.TryGetValue(path, out string hash))
            hashes.Add(path, hash = FileHash.ReadSha256(path));
        return hash;
    }

    private MethodSnapshot GetSnapshot(string path)
    {
        if (!snapshots.TryGetValue(path, out MethodSnapshot snapshot))
            snapshots.Add(path, snapshot = MethodSnapshot.Read(path));
        return snapshot;
    }

    private MethodSnapshot InstalledSnapshot()
    {
        string gameDll = Path.Combine(managed, MethodSnapshot.AssemblyFile);
        string hash = HashOf(gameDll);
        string path = Path.Combine(cache, "game", hash + MethodSnapshot.Extension);
        MethodSnapshot snapshot = TryRead(path, MethodSnapshot.Read);
        if (snapshot?.AssemblyHash == hash)
            return snapshot;
        snapshot = MethodSnapshot.Scan(appVersion, File.ReadAllBytes(gameDll));
        if (snapshot.AssemblyHash != hash)
            throw new IOException("Sea Power's assembly changed during the check.");
        try
        {
            snapshot.Write(path);
            // One install has one game build: snapshots of earlier builds are never read again.
            foreach (string old in Directory.GetFiles(Path.GetDirectoryName(path), "*" + MethodSnapshot.Extension))
                if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase))
                    File.Delete(old);
        }
        catch (Exception ex)
        {
            log($"Could not cache the installed game snapshot: {ex.Message}");
        }
        return snapshot;
    }

    // Results of other game builds, mod versions or Keel builds are never read again; the ones in use are touched on every hit.
    private bool pruned;

    private void Prune()
    {
        if (pruned)
            return;
        pruned = true;
        string results = Path.Combine(cache, "results");
        DateTime cutoff = DateTime.UtcNow.AddDays(-30);
        foreach (string folder in Directory.GetDirectories(results))
        {
            foreach (string file in Directory.GetFiles(folder))
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            if (!Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
        }
    }

    internal static string[] Changes(Preflight.Result result, MethodSnapshot current, MethodSnapshot[] previous)
    {
        if (previous.Length == 0)
            return [];
        string versions = string.Join(", ", previous.Select(p => p.Build).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));
        var warnings = new List<string>();
        foreach (string target in result.PatchTargets.Union(result.LookupTargets).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!current.Methods.TryGetValue(target, out MethodSnapshot.Fingerprint now))
                continue;
            MethodSnapshot.Fingerprint[] older = previous.Select(p => p.Methods.TryGetValue(target, out var entry) ? entry : null).ToArray();
            if (older.Any(x => x == null))
                continue;
            bool body = older.All(x => !now.Body.SequenceEqual(x.Body));
            // Both graph hashes include the root. Only assign an indirect change when the root stayed the same.
            bool sameBody = older.All(x => now.Body.SequenceEqual(x.Body));
            bool callers = sameBody && older.All(x => !now.Callers.SequenceEqual(x.Callers));
            bool callees = sameBody && older.All(x => !now.Callees.SequenceEqual(x.Callees));
            string change = body ? "its code or metadata"
                : callers && callees ? "its upstream callers and downstream callees"
                : callers ? "its upstream callers"
                : callees ? "its downstream callees" : null;
            if (change != null)
            {
                string relation = result.PatchTargets.Contains(target) ? "Harmony patch target" : "AccessTools lookup";
                warnings.Add($"{relation} {target} has changes in {change} since Sea Power {versions}.");
            }
        }
        const int limit = 12;
        return warnings.Count <= limit ? warnings.ToArray()
            : warnings.Take(limit).Append($"Method history found {warnings.Count - limit} more changed targets or lookups.").ToArray();
    }

    private T TryRead<T>(string path, Func<string, T> read) where T : class
    {
        if (!File.Exists(path))
            return null;
        try
        {
            return read(path);
        }
        catch (Exception ex)
        {
            log($"Could not read method history cache {Path.GetFileName(path)}: {ex.Message}");
            return null;
        }
    }

    private static Review ReadReview(string path)
    {
        using var reader = MethodSnapshot.Open(path);
        if (reader.ReadString() != ResultFormat)
            throw new InvalidDataException("Unknown preflight cache format.");
        string[] ReadList() => Enumerable.Range(0, MethodSnapshot.Count(reader, 100_000)).Select(_ => reader.ReadString()).ToArray();
        var result = new Preflight.Result();
        result.ClassLoad.AddRange(ReadList());
        result.Uses.AddRange(ReadList());
        result.Lookups.AddRange(ReadList());
        result.Fatal.AddRange(ReadList());
        return new Review(result, ReadList());
    }

    private static void WriteReview(BinaryWriter writer, Preflight.Result result, string[] warnings)
    {
        void WriteList(IReadOnlyList<string> items)
        {
            writer.Write(items.Count);
            foreach (string item in items)
                writer.Write(item);
        }
        writer.Write(ResultFormat);
        WriteList(result.ClassLoad);
        WriteList(result.Uses);
        WriteList(result.Lookups);
        WriteList(result.Fatal);
        WriteList(warnings);
    }
}
