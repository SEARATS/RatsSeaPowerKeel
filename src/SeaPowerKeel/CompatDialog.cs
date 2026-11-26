using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;

namespace SeaPowerKeel;

/// <summary>
/// Game 0.9.0 asks at every start about switched-on mods whose <c>_info.ini</c> names another game version, as one long list of names
/// and full paths. Keel rewrites that list: short names, code mods apart from data mods, the version each one says it was made for,
/// and what Keel does about code mods. The game's own first line (in the player's language) and its buttons stay. While the dialog
/// waits for a click, Keel's loading window says so instead of counting the seconds since the last message.
/// Hooked on the message box's Message setter, a plain class with no UI statics, so it is safe from Awake on every build; a build
/// without the dialog never sets that text.
/// The setting VersionQuestion can skip the question (a player wish 2026-11-26: most mods never update the line, and Keel
/// checks every DLL against the running build anyway). Skipping is the game's own Ignore: Ignore only closes the box and logs a warning.
/// </summary>
internal static class CompatDialog
{
    private static string header;       // the game's own first line, LoadScreen/WarningMods
    private static object box;          // the dialog Keel rewrote, until it is answered
    private static Type commandType;
    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    public const string Ask = "Ask", Auto = "Auto", Skip = "Skip";
    private static string mode = Auto;

    /// <summary>What Keel answered for the player at this start, for the report; null when the game asked (or had nothing to ask).</summary>
    public static string SkipNote { get; private set; }

    /// <summary>Null when hooked, else why not. <paramref name="question"/>: the VersionQuestion setting.</summary>
    public static string Install(string question)
    {
        mode = question;
        try
        {
            Type boxType = AccessTools.TypeByName("SeapowerUI.ViewModels.MessageBoxViewModel");
            commandType = AccessTools.TypeByName("SeaPower.DelegateCommand");
            if (boxType == null || commandType == null)
                return "the game's message box is not in this build";
            var harmony = new Harmony("dev.sewerlabs.seapowerkeel.compatdialog");
            harmony.Patch(AccessTools.PropertySetter(boxType, "Message"), prefix: new HarmonyMethod(typeof(CompatDialog).GetMethod(nameof(MessagePrefix), Any)));
            MethodInfo headerSetter = AccessTools.PropertySetter(boxType, "Header");
            if (headerSetter != null)
                harmony.Patch(headerSetter, prefix: new HarmonyMethod(typeof(CompatDialog).GetMethod(nameof(HeaderPrefix), Any)));
            foreach (string command in new[] { "AcknowledgeCommand", "CancelCommand" })
            {
                MethodInfo setter = AccessTools.PropertySetter(boxType, command);
                if (setter != null)
                    harmony.Patch(setter, prefix: new HarmonyMethod(typeof(CompatDialog).GetMethod(nameof(CommandPrefix), Any)));
            }
            MethodInfo check = AccessTools.Method(AccessTools.TypeByName("SeaPower.FileManager"), "CheckCompatibility");
            if (check != null)
                harmony.Patch(check, prefix: new HarmonyMethod(typeof(CompatDialog).GetMethod(nameof(CheckPrefix), Any)));
            else if (mode != Ask)
                Keel.Log.LogInfo("The game's version question cannot be skipped in this build: it is asked as before.");
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    // Before the game builds its question: Keel answers Ignore itself when the setting says so. Auto skips it only when every listed
    // mod has DLLs, which Keel checks against this build next; a data mod (units, sounds, missions) Keel cannot check, so the game asks.
    private static bool CheckPrefix(object __instance, ref IEnumerator __result)
    {
        try
        {
            if (mode == Ask)
                return true;
            (List<string> code, List<string> data) = Listed(__instance);
            if (code.Count + data.Count == 0 || (mode == Auto && data.Count > 0))
                return true;
            SkipNote = SkipText(code, data);
            Keel.Log.LogInfo($"The game's mod version question was skipped (VersionQuestion = {mode}), as if Ignore was clicked. {SkipNote}");
            __result = Answered();
            return false;
        }
        catch (Exception)
        {
            return true;    // the game asks as before
        }
    }

    // The game's coroutine after Ignore: nothing left to do but one frame.
    private static IEnumerator Answered()
    {
        yield return null;
    }

    private static string SkipText(List<string> code, List<string> data)
    {
        var sb = new StringBuilder($"Keel answered the game's version question with Ignore (VersionQuestion = {mode}).");
        if (code.Count > 0)
            sb.Append(" Code mods made for another game version, checked by Keel: ").Append(Names(code)).Append('.');
        if (data.Count > 0)
            sb.Append(" Data mods made for another game version, which Keel cannot check: ").Append(Names(data)).Append('.');
        return sb.ToString();
    }

    private static string Names(List<string> lines) => string.Join("; ", lines.Select(l => l.Replace("  •  ", "").Replace("  -  ", ", ")));

    private static void MessagePrefix(object __instance, ref string value)
    {
        try
        {
            LogBox(value);
            header ??= Text("LoadScreen", "WarningMods");
            if (string.IsNullOrEmpty(header) || value == null || !value.StartsWith(header, StringComparison.Ordinal))
            {
                if (__instance == box && box != null)
                    Unbrand(__instance);
                return;
            }
            string rewritten = Rewrite(header);
            if (rewritten == null)
                return;
            value = rewritten;
            box = __instance;
            // The top bar says where the list comes from (his wish 2026-11-12): "MESSAGE  ·  R.A.T.S. Keel".
            PropertyInfo h = __instance.GetType().GetProperty("Header", Any);
            if (h?.CanWrite == true)
                h.SetValue(__instance, Branded(h.GetValue(__instance) as string));
            Keel.Log.LogInfo("The game's mod version dialog is shown with Keel's list.");
            Loading.LoadingWindow.WaitingFor = "the game asks about mods (in the game window)";
        }
        catch (Exception)
        {
            // the game's own text stays
        }
    }

    // Every message box the game shows, in one log line: tests (and the loading window) see a box that waits for a click,
    // whoever opened it (the game's version question, a mod's error box). 2026-11-12: boxes went unclicked.
    private static void LogBox(string text)
    {
        if (string.IsNullOrEmpty(text))
            return;
        string first = text.Replace("\r", "").Replace("\n", " / ");
        Keel.Log.LogInfo("Game message box: " + (first.Length > 140 ? first.Substring(0, 140) + "..." : first));
    }

    // The game shows another message in the box Keel branded: the brand goes.
    private static void Unbrand(object instance)
    {
        box = null;
        PropertyInfo old = instance.GetType().GetProperty("Header", Any);
        if (old?.CanWrite == true && old.GetValue(instance) is string was && was.EndsWith(BrandSuffix))
            old.SetValue(instance, was.Substring(0, was.Length - BrandSuffix.Length));
    }

    private const string BrandSuffix = "  ·  " + Keel.Name;

    internal static string Branded(string header)
    {
        string h = string.IsNullOrEmpty(header) ? "MESSAGE" : header;
        return h.Contains(Keel.Name) ? h : h + BrandSuffix;
    }

    // The header can be set after the text: it keeps the brand on Keel's dialog.
    private static void HeaderPrefix(object __instance, ref string value)
    {
        if (__instance == box && box != null)
            value = Branded(value);
    }

    // The dialog's buttons are set after its text: wrapped so the loading window stops waiting when one is clicked.
    private static void CommandPrefix(object __instance, ref object value)
    {
        if (__instance != box || value == null || !(value is System.Windows.Input.ICommand inner))
            return;
        Action<object> then = x =>
        {
            Loading.LoadingWindow.WaitingFor = null;
            box = null;
            inner.Execute(x);
        };
        try
        {
            value = Activator.CreateInstance(commandType, then);
        }
        catch (Exception)
        {
            // the game's own command stays
        }
    }

    private static string Text(string section, string key)
    {
        object instance = Reflected.Singleton("SeaPower.LanguageResourceHandler");
        // getText(section, key) in older builds, getText(section, key, logError, verboseError) since 0.9.0: fill what follows with defaults.
        MethodInfo get = instance?.GetType().GetMethods(Any).FirstOrDefault(m => m.Name == "getText" && m.GetParameters().Length >= 2
                                                                  && m.GetParameters()[0].ParameterType == typeof(string) && m.GetParameters()[1].ParameterType == typeof(string));
        if (instance == null || get == null)
            return null;
        object[] args = get.GetParameters().Select((p, i) => i == 0 ? section : i == 1 ? key : p.HasDefaultValue ? p.DefaultValue : (p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)).ToArray();
        return get.Invoke(instance, args) as string;
    }

    /// <summary>The new text from the game's own list (the same test the game uses), or null.</summary>
    private static string Rewrite(string first)
    {
        UnityEngine.Object manager = GameState.Find(AccessTools.TypeByName("SeaPower.FileManager"));
        if (manager == null)
            return null;
        (List<string> code, List<string> data) = Listed(manager);
        if (code.Count + data.Count == 0)
            return null;
        var sb = new StringBuilder(first).Append("\n");
        if (code.Count > 0)
            sb.Append("\nCode mods\n").Append(string.Join("\n", code)).Append("\n");
        if (data.Count > 0)
            sb.Append("\nData mods  (units, sounds, pictures, missions)\n").Append(string.Join("\n", data)).Append("\n");
        sb.Append("\nIgnore keeps them on; the other button switches them off and reloads.");
        return sb.ToString();
    }

    // The game's own list of switched-on mods made for another version (the same test the game uses), split into code and data mods.
    private static (List<string> code, List<string> data) Listed(object manager)
    {
        var code = new List<string>();
        var data = new List<string>();
        Type t = manager.GetType();
        object list = t.GetProperty("Directories", Any)?.GetValue(manager) ?? t.GetField("directories", Any)?.GetValue(manager);
        if (list is IEnumerable dirs)
            foreach (object d in dirs)
            {
                (string line, bool hasCode) = MismatchLine(d);
                if (line != null)
                    (hasCode ? code : data).Add(line);
            }
        return (code, data);
    }

    // One of the game's mod folders: its list line when it is switched on and does not match this game version, else null.
    private static (string line, bool hasCode) MismatchLine(object d)
    {
        if (d == null)
            return (null, false);
        Type t = d.GetType();
        if (!OnAndOffVersion(d, t))
            return (null, false);
        string name = t.GetProperty("Name", Any)?.GetValue(d) as string;
        string path = (t.GetProperty("DirectoryInfo", Any)?.GetValue(d) as DirectoryInfo)?.FullName;
        if (string.IsNullOrEmpty(name))
            name = Path.GetFileName(path ?? "?");
        return ("  •  " + name + Declared(t.GetField("_compatibility", Any)?.GetValue(d)), HasCode(path));
    }

    // A mod folder the game counts (a settings mod), switched on, whose _info.ini does not match this game version.
    private static bool OnAndOffVersion(object d, Type t)
    {
        bool settingsMod = t.GetProperty("IsSettingsMod", Any)?.GetValue(d) as bool? ?? false;
        bool on = t.GetProperty("IsChecked", Any)?.GetValue(d) as bool? ?? false;
        bool matches = t.GetMethod("MatchesVersion", Any, null, Type.EmptyTypes, null)?.Invoke(d, null) as bool? ?? true;
        return settingsMod && on && !matches;
    }

    private static bool HasCode(string path)
    {
        try
        {
            return path != null && Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories).Any();
        }
        catch (Exception)
        {
            return false;
        }
    }

    // What the mod's _info.ini says, in the game's own terms.
    private static string Declared(object c)
    {
        if (c == null)
            return "";
        string Field(string n) => c.GetType().GetField(n, Any)?.GetValue(c) as string;
        string exact = Field(InfoVersion.ExactKey), approx = Field(InfoVersion.ApproximateKey);
        if (!string.IsNullOrEmpty(exact))
            return $"  -  made for {exact} only";
        if (!string.IsNullOrEmpty(approx))
            return $"  -  made for {approx}";
        List<string> parts = InfoVersion.Bounds
            .SelectMany(b => new[] { (b.OrEqualKey, b.OrEqualSign), (b.StrictKey, b.StrictSign) })
            .Select(k => (sign: k.Item2, value: Field(k.Item1)))
            .Where(k => !string.IsNullOrEmpty(k.value))
            .Select(k => k.sign + " " + k.value).ToList();
        return parts.Count > 0 ? "  -  made for game " + string.Join(", ", parts) : "  -  excludes this version";
    }
}
