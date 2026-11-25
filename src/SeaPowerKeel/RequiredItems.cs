using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace SeaPowerKeel;

/// <summary>A Workshop item a mod needs, as the mod's <c>_info.ini</c> names it.</summary>
internal sealed class RequiredItem
{
    public ulong WorkshopId;
    public string PluginId;
    public string Name;

    /// <summary>The item's Workshop page, opened in the Steam client.</summary>
    public string PageUrl => $"steam://url/CommunityFilePage/{WorkshopId}";
}

/// <summary>
/// A mod names the Workshop items it needs in its <c>_info.ini</c>, one line per item in the <c>[Keel]</c> section:
/// <code>
/// [Keel]
/// Requires=3840000000, some.plugin.id, The Item's Name
/// </code>
/// The three values are the item's Workshop id, the plugin id the mod lists in its dependency, and a name for the card. Keel uses
/// it for two things, for any mod and any item: a mod kept out because that plugin is missing or too old gets a "Get it"
/// button on the card and in the report, and an item that is installed but switched off in the mod list is switched on at the
/// start when a mod that is switched on needs it. Steam gives a new required item only to new subscribers, which is why.
/// </summary>
internal static class RequiredItems
{
    public const string InfoKey = "Requires";

    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>Every <c>Requires</c> line of the <c>[Keel]</c> section of <paramref name="infoIni"/> (the text of a mod's _info.ini).</summary>
    public static List<RequiredItem> InInfoIni(string infoIni)
    {
        var items = new List<RequiredItem>();
        bool inKeel = false;
        foreach (string raw in (infoIni ?? "").Split('\n'))
        {
            string line = raw.Split(';', '#')[0].Trim();
            if (line.StartsWith("["))
            {
                inKeel = line.Equals("[Keel]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            int eq = line.IndexOf('=');
            if (!inKeel || eq <= 0)
                continue;
            string key = line.Substring(0, eq).Trim();
            if (!key.StartsWith(InfoKey, StringComparison.OrdinalIgnoreCase) || !key.Substring(InfoKey.Length).All(char.IsDigit))
                continue;
            string[] parts = line.Substring(eq + 1).Split(new[] { ',' }, 3);
            if (parts.Length < 2 || !ulong.TryParse(parts[0].Trim(), out ulong id) || id == 0 || parts[1].Trim().Length == 0)
                continue;
            string pluginId = parts[1].Trim();
            string name = parts.Length > 2 && parts[2].Trim().Length > 0 ? parts[2].Trim() : pluginId;
            items.Add(new RequiredItem { WorkshopId = id, PluginId = pluginId, Name = name });
        }
        return items;
    }

    /// <summary>The items the folder's _info.ini names (none when there is no file or it cannot be read).</summary>
    public static List<RequiredItem> OfFolder(string folder)
    {
        try
        {
            string path = Path.Combine(folder ?? "", "_info.ini");
            return File.Exists(path) ? InInfoIni(File.ReadAllText(path)) : new List<RequiredItem>();
        }
        catch (Exception)
        {
            return new List<RequiredItem>();
        }
    }

    /// <summary>
    /// Required items that are in the mod list but switched off, while a mod that is switched on names them: the folders to switch on.
    /// <paramref name="requiredBy"/> gives the items a folder's mod needs.
    /// </summary>
    public static List<ModFolder> ToSwitchOn(IEnumerable<ModFolder> listed, Func<ModFolder, IEnumerable<RequiredItem>> requiredBy)
    {
        List<ModFolder> all = listed.ToList();
        var wanted = new HashSet<ulong>(all.Where(f => f.Enabled).SelectMany(requiredBy).Select(r => r.WorkshopId));
        return all.Where(f => !f.Enabled && f.SteamId != 0 && wanted.Contains(f.SteamId)).ToList();
    }

    /// <summary>
    /// Switches on, in the game's mod list, every required item that is installed but off (the way the Mods menu does: the entry
    /// ticked and the list saved). Returns the folders switched on; the list's own entries are updated to match. Never throws.
    /// </summary>
    public static List<ModFolder> SwitchOn(IList<ModFolder> listed, Action<string> info, Action<string> warn)
    {
        List<ModFolder> off = ToSwitchOn(listed, f => OfFolder(f.Path));
        if (off.Count == 0)
            return off;
        try
        {
            object files = Reflected.Singleton("SeaPower.FileManager");
            object directories = files?.GetType().GetField("directories", Any)?.GetValue(files);
            if (files == null || directories is not IEnumerable entries)
                throw new InvalidOperationException("the game's mod list is not there yet");
            var switched = new List<ModFolder>();
            foreach (object entry in entries)
            {
                ModFolder folder = off.FirstOrDefault(f => f.SteamId != 0 && SteamIdOf(entry) == f.SteamId);
                if (folder == null)
                    continue;
                entry.GetType().GetProperty("IsChecked", Any).SetValue(entry, true);
                folder.Enabled = true;
                switched.Add(folder);
            }
            if (switched.Count > 0)
                files.GetType().GetMethod("SaveDirectories", Any).Invoke(files, new[] { directories });
            foreach (ModFolder f in switched)
                info($"Required item switched on in the mod list: {f.Label} (a mod that is on needs it).");
            return switched;
        }
        catch (Exception ex)
        {
            warn($"Could not switch on required items: {Reflected.Unwrap(ex).Message}");
            return new List<ModFolder>();
        }
    }

    private static ulong SteamIdOf(object entry)
    {
        object id = entry.GetType().GetField("steamId", Any)?.GetValue(entry);
        return id?.GetType().GetField("m_PublishedFileId", Any)?.GetValue(id) is ulong u ? u : 0;
    }

    /// <summary>The required items that keep mods out, each with how many mods wait for it (for the card).</summary>
    public static List<(RequiredItem item, int mods)> Missing(IEnumerable<PluginEntry> all) =>
        all.Where(p => p.MissingItem != null && ReportFormat.IsProblem(p.State))
           .GroupBy(p => p.MissingItem.WorkshopId)
           .Select(g => (g.First().MissingItem, g.Count()))
           .ToList();
}
