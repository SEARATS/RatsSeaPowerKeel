using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SeaPowerKeel;

// Each startup stage owns a separate journal and writes it before invoking a helper.
internal class StartupJournal
{
    private readonly string path;
    private readonly Dictionary<string, string> held = new(StringComparer.OrdinalIgnoreCase);

    public StartupJournal(string path, bool hold, Action<string> note)
    {
        this.path = path;
        try
        {
            if (File.Exists(path))
                foreach (string line in File.ReadLines(path))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length != 3)
                        continue;
                    if (parts[0] == "running")
                        note($"The last game ended while startup helper {Path.GetFileName(parts[1])} was running.");
                    if (hold && parts[0] is "running" or "held")
                        held[parts[1]] = parts[2];
                }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        Save();
    }

    public bool CheckHeld(string dll, Action<string> note)
    {
        if (!held.TryGetValue(dll, out string stamp) || stamp != Stamp(dll))
            return false;
        note($"Skipped {Path.GetFileName(dll)} after the last game ended while it was running. " +
             $"It can run again after a mod update or after clearing {Path.GetFileName(path)}.");
        return true;
    }

    public void Start(string dll)
    {
        held.Remove(dll);
        Save(dll);
    }

    public void Save(string running = null)
    {
        try
        {
            if (running == null && held.Count == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            IEnumerable<string> lines = held.Select(h => $"held\t{h.Key}\t{h.Value}");
            if (running != null)
                lines = lines.Append($"running\t{running}\t{Stamp(running)}");
            string temporary = path + ".tmp";
            File.WriteAllLines(temporary, lines);
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }
        catch (Exception)
        {
            // Startup can proceed when the journal directory is unwritable.
        }
    }

    public static string Stamp(string dll)
    {
        try
        {
            var file = new FileInfo(dll);
            return file.LastWriteTimeUtc.Ticks + ":" + file.Length;
        }
        catch (Exception)
        {
            return "";
        }
    }
}
