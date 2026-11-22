using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SeaPowerKeel;

/// <summary>
/// The game's own mod list in usersettings.ini. Keel only changes it when it starts: before the game reads the file, and the game
/// writes its list back at exit, so a change made during a game would be lost. Kept apart from Unity so it can be tested offline.
/// </summary>
internal static class ModList
{
    private static readonly Regex ModLine = new(@"^(Mod\d+Directory=)([^,\r\n]+),(True|False),(.*)$", RegexOptions.Compiled);

    /// <summary>Carries out the crash window's choice to keep a pack off. Returns the folder, or null.</summary>
    internal static string ApplyPackOff(string dir, string settingsPath)
    {
        string file = Path.Combine(dir, KeelNames.PackOffFile);
        try
        {
            if (!File.Exists(file))
                return null;
            string folder = File.ReadAllText(file).Trim();
            File.Delete(file);
            if (folder.Length == 0 || !File.Exists(settingsPath))
                return null;
            File.WriteAllText(settingsPath, SwitchOff(File.ReadAllText(settingsPath), folder));
            return folder;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>The settings with <paramref name="folder"/> switched off in the mod list. Other lines stay as they are.</summary>
    internal static string SwitchOff(string settings, string folder)
    {
        string newline = settings.Contains("\r\n") ? "\r\n" : "\n";
        IEnumerable<string> lines = settings.Replace("\r\n", "\n").Split('\n').Select(line =>
        {
            Match m = ModLine.Match(line);
            return m.Success && m.Groups[2].Value == folder ? $"{m.Groups[1].Value}{folder},False,{m.Groups[4].Value}" : line;
        });
        return string.Join(newline, lines);
    }
}
