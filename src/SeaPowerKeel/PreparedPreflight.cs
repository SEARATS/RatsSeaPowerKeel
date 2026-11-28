using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace SeaPowerKeel;

// Results are optional. A dispatch can take a completed check or do the normal check immediately.
internal class PreparedPreflight
{
    private class Candidate(string[] compatibility)
    {
        public string[] Compatibility { get; } = compatibility;
        public volatile Checked Ready;
    }

    private class Checked(string hash, Preflight.Result result)
    {
        public string Hash { get; } = hash;
        public Preflight.Result Result { get; } = result;
    }

    private readonly ConcurrentDictionary<string, Candidate> pending = new(StringComparer.OrdinalIgnoreCase);

    internal Task Completion { get; }

    internal PreparedPreflight(string managed, string core, string version, (string Dll, string[] Compatibility)[] files)
    {
        foreach (var (dll, compatibility) in files)
            pending.TryAdd(dll, new Candidate(compatibility));
        Completion = Task.Run(() => Run(managed, core, version, files));
    }

    private void Run(string managed, string core, string version, (string Dll, string[] Compatibility)[] files)
    {
        try
        {
            using var preflight = new Preflight(managed, core);
            using var sha = SHA256.Create();
            foreach (var (dll, _) in files)
            {
                if (!pending.TryGetValue(dll, out Candidate candidate))
                    continue;
                try
                {
                    // The method-history path handles declared mismatches, including its own result cache.
                    if (InfoVersion.Mismatch(candidate.Compatibility, version) != null)
                        continue;
                    byte[] image = File.ReadAllBytes(dll);
                    Preflight.Result result = preflight.Check(dll, image);
                    if (!result.PatchCheckIncomplete)
                        candidate.Ready = new Checked(FileHash.Hex(sha.ComputeHash(image)), result);
                }
                catch (Exception)
                {
                    // The regular preflight reports unreadable or changed DLLs.
                }
            }
        }
        catch (Exception)
        {
            // With no worker results the regular preflight still checks every DLL.
        }
    }

    internal Preflight.Result Take(string dll, bool useResult = true) =>
        pending.TryRemove(dll, out Candidate candidate) && useResult && candidate.Ready is Checked ready
        && ready.Hash == FileHash.Sha256(dll) ? ready.Result : null;

    internal void Discard() => pending.Clear();
}
