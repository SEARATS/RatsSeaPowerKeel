using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Noesis;

namespace SeaPowerKeel;

/// <summary>
/// Keel's own tab in the game's Options menu (his wish 2026-11-26): the settings players change, drawn by the game in its own
/// styles; the rest stays in the config file. When an options page (SeapowerUI.OptionsMenuView) has loaded, a tab named after Keel
/// is added to its tab control. Changes are written to the config file at once; most of them are read while the game starts, so
/// the tab says they apply at the next start. The page's Accept / Revert do not touch them.
/// Noesis types are used only in this class, and it is first touched once the game's UI is up (see Keel.HookModMenu): touching
/// Noesis before that kills the game in native code. Same way of building as Rigging's RigOptions; both become one with Keel's
/// shared settings page (Wave 3, docs/community/ROADMAP.md).
/// </summary>
internal static class KeelOptions
{
    private const string Tag = "SeaPowerKeel.Options";
    private static ConfigFile config;
    private static bool reported;

    /// <summary>Null when hooked, else why not.</summary>
    public static string Install(ConfigFile file)
    {
        config = file;
        try
        {
            Type view = AccessTools.TypeByName("SeapowerUI.OptionsMenuView");
            ConstructorInfo ctor = view == null ? null : AccessTools.Constructor(view, Type.EmptyTypes);
            if (ctor == null)
                return "the game's Options page is not in this build";
            new Harmony("dev.sewerlabs.seapowerkeel.options").Patch(ctor, postfix: new HarmonyMethod(typeof(KeelOptions), nameof(ViewCreated)));
            return null;
        }
        catch (Exception ex)
        {
            return $"{ex.GetType().Name}: {ex.Message}";
        }
    }

    private static void ViewCreated(object __instance)
    {
        try
        {
            if (__instance is FrameworkElement view)
                view.Loaded += (s, e) => Attach(view);
        }
        catch (Exception ex)
        {
            Keel.Log.LogWarning("Options page: " + ex.Message);
        }
    }

    private static void Attach(FrameworkElement view)
    {
        try
        {
            TabControl tabs = Find<TabControl>(view, 24);
            if (tabs == null)
            {
                Report("Options menu layout not recognised (game update?): Keel's settings are only in its config file.", warn: true);
                return;
            }
            foreach (object item in tabs.Items)
                if (item is FrameworkElement fe && fe.Tag as string == Tag)
                    return;
            var panel = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(20, 16, 20, 16) };
            Fill(new OptionsRows(view, panel));
            var tab = new TabItem
            {
                Tag = Tag,
                Header = Keel.Name,
                Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = panel },
            };
            foreach (object item in tabs.Items)
                if (item is TabItem other && other.Style != null)
                {
                    tab.Style = other.Style;
                    break;
                }
            tabs.Items.Add(tab);
            Report("Keel's settings added as a tab of the Options menu.");
        }
        catch (Exception ex)
        {
            Report("Keel's settings not added to the Options menu: " + ex.Message, warn: true);
        }
    }

    private static void Fill(OptionsRows rows)
    {
        rows.Note("Most settings apply at the next start: Keel reads them while the game starts. Everything else is in BepInEx\\config\\dev.sewerlabs.seapowerkeel.cfg.");

        rows.Header("Mod checks");
        VersionQuestionRows(rows);
        rows.Choice("Game version check", Entry<string>(Takeover.ConfigSection, "GameVersionCheck"), new[] { "Standard", "Strict", "Off" },
            "Standard: leave out DLLs that would stop missions from loading, warn about the rest. Strict: leave out any DLL with a missing part. Off: no check.");
        rows.Check("Remove a mod's broken patches", Entry<bool>(Takeover.ConfigSection, "RemoveBrokenPatches"),
            "A patch that fails on something this game build lacks is taken off that method, and the report names the mod.");
        rows.Check("Keep out mods that crashed the game", Entry<bool>(Takeover.ConfigSection, "HoldOutCrashers"),
            "A mod that took the game down stays out until it is updated. Off: Keel only names it.");

        rows.Header("Cards and report");
        rows.Check("Show What's new after mod updates", Entry<bool>("WhatsNew", "Show"), "A card on the main menu with what each author wrote about the update.");
        rows.Check("Read change notes from the Workshop", Entry<bool>("WhatsNew", "WorkshopNotes"),
            "For updated Workshop mods, the author's notes from the item's Steam changelog page. Off: the mod's CHANGELOG.md or just the version.");
        rows.Check("Notice when a mod has problems", Entry<bool>("Report", "NoticeOnProblems"), "A short notice in the corner when a mod did not start or threw errors.");
        rows.Slider("Report window size", "0 = by screen height.", Entry<float>("Report", "Scale"), 0f, 4f, 0.25f, Auto);

        rows.Header("Loading window");
        rows.Check("Show the loading window", Entry<bool>("Loading window", "Show"), "The small window with the start-up steps and the mods as they start.");
        rows.Choice("Position", Entry<string>("Loading window", "Spot"), new[] { "TopLeft", "TopRight", "BottomLeft", "BottomRight", "Marker" },
            "A corner of the game's window, or Marker: the band under the main menu's news box.");
        rows.Slider("Size", "0 = by the height of the game's window.", Entry<float>("Loading window", "Scale"), 0f, 4f, 0.25f, Auto);
        rows.Slider("See-through", "0.4 = see-through, 1 = solid.", Entry<float>("Loading window", "Opacity"), 0.4f, 1f, 0.05f, v => v.ToString("0.00"));

        rows.Header("Logging");
        rows.Check("Write the timestamped log", Entry<bool>("Logging", "TimestampedLog"),
            "BepInEx\\Keel-log.txt: every log line with its time and the mod an exception came from.");
    }

    // One setting, two boxes: the first switches Keel's answer on (Auto), the second, only open while the first is on, widens it to
    // lists with data mods (Skip).
    private static void VersionQuestionRows(OptionsRows rows)
    {
        ConfigEntry<string> question = Entry<string>(Takeover.ConfigSection, "VersionQuestion");
        if (question == null)
            return;
        CheckBox wider = null;
        rows.Check("Answer the game's version question for me (code mods)", question.Value != CompatDialog.Ask, on =>
        {
            question.Value = !on ? CompatDialog.Ask : wider?.IsChecked == true ? CompatDialog.Skip : CompatDialog.Auto;
            if (wider != null)
                wider.IsEnabled = on;
        }, "Keel clicks Ignore when the game asks about mods made for another game version, and checks their DLLs against this build itself.");
        wider = rows.Check("also when data mods are on the list", question.Value == CompatDialog.Skip,
            on => question.Value = on ? CompatDialog.Skip : CompatDialog.Auto,
            "Data mods (units, sounds, missions) Keel cannot check; the report still names them.", indent: 28);
        wider.IsEnabled = question.Value != CompatDialog.Ask;
    }

    private static string Auto(float v) => v <= 0f ? "auto" : v.ToString("0.00");

    private static ConfigEntry<T> Entry<T>(string section, string key) =>
        config != null && config.TryGetEntry(new ConfigDefinition(section, key), out ConfigEntry<T> entry) ? entry : null;

    private static void Report(string text, bool warn = false)
    {
        if (reported)
            return;
        reported = true;
        if (warn)
            Keel.Log.LogWarning(text);
        else
            Keel.Log.LogInfo(text);
    }

    private static T Find<T>(DependencyObject root, int maxDepth) where T : DependencyObject
    {
        var queue = new Queue<(DependencyObject node, int depth)>();
        queue.Enqueue((root, 0));
        while (queue.Count > 0)
        {
            var (node, depth) = queue.Dequeue();
            if (node is T hit)
                return hit;
            if (depth >= maxDepth)
                continue;
            int n = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < n; i++)
                queue.Enqueue((VisualTreeHelper.GetChild(node, i), depth + 1));
        }
        return null;
    }
}

/// <summary>Adds rows to Keel's tab, in the page's own styles where it has them. A row whose setting is missing is left out.</summary>
internal sealed class OptionsRows
{
    private readonly StackPanel panel;
    private readonly Style headerStyle, labelStyle;
    private readonly Brush highlight;
    private readonly float noteSize;

    public OptionsRows(FrameworkElement view, StackPanel panel)
    {
        this.panel = panel;
        headerStyle = view.TryFindResource("OptionsSectionHeader") as Style;
        labelStyle = view.TryFindResource("TextBlockLabel") as Style;
        highlight = view.TryFindResource("Brush.Foreground.Highlight") as Brush;
        object size = view.TryFindResource("Font.Size.ToolTip");
        noteSize = size is float f ? f : size is double d ? (float)d : 0f;
    }

    public void Header(string text)
    {
        var header = new TextBlock { Text = text, Margin = new Thickness(0, 14, 0, 8), FontSize = 18f };
        if (headerStyle != null)
            header.Style = headerStyle;
        else if (highlight != null)
            header.Foreground = highlight;
        panel.Children.Add(header);
    }

    public void Note(string text)
    {
        var note = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), Opacity = 0.8f };
        if (highlight != null)
            note.Foreground = highlight;
        if (noteSize > 0f)
            note.FontSize = noteSize;
        panel.Children.Add(note);
    }

    public void Check(string label, ConfigEntry<bool> entry, string tip)
    {
        if (entry != null)
            Check(label, entry.Value, on => entry.Value = on, tip);
    }

    public CheckBox Check(string label, bool value, Action<bool> changed, string tip, float indent = 0f)
    {
        var box = new CheckBox { Content = label, Margin = new Thickness(indent, 0, 0, 12), IsChecked = value, ToolTip = tip };
        if (highlight != null)
            box.Foreground = highlight;
        box.Click += (s, e) => changed(box.IsChecked == true);
        panel.Children.Add(box);
        return box;
    }

    public void Choice(string label, ConfigEntry<string> entry, IList<string> values, string tip)
    {
        if (entry == null)
            return;
        Grid row = Row(label, tip);
        var combo = new ComboBox { MinWidth = 240, HorizontalAlignment = HorizontalAlignment.Left };
        foreach (string v in values)
            combo.Items.Add(v);
        combo.SelectedIndex = Math.Max(0, values.IndexOf(entry.Value));
        combo.SelectionChanged += (s, e) =>
        {
            int i = combo.SelectedIndex;
            if (i >= 0 && entry.Value != values[i])
                entry.Value = values[i];
        };
        Grid.SetColumn(combo, 1);
        row.Children.Add(combo);
        panel.Children.Add(row);
    }

    /// <summary>A slider in steps of <paramref name="step"/>, its value shown through <paramref name="format"/>.</summary>
    public void Slider(string label, string tip, ConfigEntry<float> entry, float min, float max, float step, Func<float, string> format)
    {
        if (entry == null)
            return;
        Grid row = Row(label, tip);
        var slider = new Noesis.Slider
        {
            Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true,
            Value = Math.Max(min, Math.Min(max, entry.Value)), MinWidth = 200,
        };
        Grid.SetColumn(slider, 1);
        var value = new TextBlock { Text = format(slider.Value), MinWidth = 64, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        if (highlight != null)
            value.Foreground = highlight;
        Grid.SetColumn(value, 2);
        slider.ValueChanged += (s, e) =>
        {
            float v = (float)(Math.Round(slider.Value / step) * step);
            value.Text = format(v);
            if (Math.Abs(entry.Value - v) > step * 0.01f)
                entry.Value = v;
        };
        row.Children.Add(slider);
        row.Children.Add(value);
        panel.Children.Add(row);
    }

    // A label in the first column; the control goes into column 1 (and a slider's value into column 2).
    private Grid Row(string label, string tip)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 12), ToolTip = tip };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200f) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        if (labelStyle != null)
            text.Style = labelStyle;
        else if (highlight != null)
            text.Foreground = highlight;
        Grid.SetColumn(text, 0);
        row.Children.Add(text);
        return row;
    }
}
