using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SeaPowerKeel;

/// <summary>
/// KeelWatcher.exe: started by Keel, waits for the game to end. A game that ended without a normal quit gets a window right away that
/// says which mod did it, lets the player keep that mod off or on, and offers a restart. It lives outside the game because nothing
/// inside a crashed process can show anything. Arguments: the game's process id, the BepInEx folder, the game's exe.
/// </summary>
internal static class Watcher
{
    private const string Title = "R.A.T.S. Keel";

    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length >= 2 && args[0] == "--logs")      // the report window's Save logs; a third argument saves there and shows nothing
        {
            string zip = LogBundle.Save(args[1], args.Length > 2 ? args[2] : null);
            if (args.Length == 2)
                LogBundle.Reveal(zip);
            return 0;
        }
        if (args.Length >= 2 && args[0] == "--send")      // the report window's Send report: the send window, then nothing else
        {
            StartWindows();
            Sender.Run(args[1], args.Length > 2 ? args[2] : null);
            return 0;
        }
        if (args.Length == 3 && args[0] == "--relaunch")      // the report window's Restart the game: wait for the game to quit, then start it
        {
            if (int.TryParse(args[1], out int old))
                WaitFor(old);
            Restart(args[2]);
            return 0;
        }
        if (args.Length < 3 || !int.TryParse(args[0], out int pid))
            return 2;
        Watch(pid, args[1], args[2]);
        return 0;
    }

    // Sharp text on a 4K screen.
    private static void StartWindows()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
    }

    // The game's whole life: wait, then the crash window when it did not end the normal way, and what the player chose.
    private static void Watch(int pid, string dir, string exe)
    {
        string why = WaitFor(pid);
        string[] lines = CrashText.ReadLines(Path.Combine(dir, KeelNames.JournalFile));
        CrashText.Journal journal = CrashText.ReadJournal(lines);
        string stack = CrashText.ReadAll(Path.Combine(dir, KeelNames.StackFile));
        if (!journal.Unclean)
        {
            Note(dir, $"game {pid} ended ({why}), quit mark found: no window");
            return;
        }
        if (CrashText.ClosedNormally(stack, Path.Combine(KeelNames.PlayerDataDir, "Player.log")))
        {
            Note(dir, $"game {pid} ended ({why}) without a quit mark, but Unity shut down normally: no window");
            return;
        }
        Note(dir, $"game {pid} ended ({why}) without a quit mark: crash window");

        string text = Explain(journal, dir, out string guid, out string name, out string detail, out string pack);
        WriteCrashFile(journal, dir, stack);
        StartWindows();
        bool restart = Ask(text, name, detail, dir, pack != null, out bool keepOff);
        if (guid != null && !keepOff)
            TryWrite(Path.Combine(dir, KeelNames.KeepFile), guid);       // else the mod is held out as usual
        if (pack != null && keepOff)
            TryWrite(Path.Combine(dir, KeelNames.PackOffFile), pack);    // else the pack stays on
        if (restart)
            Restart(exe);
    }

    private static void TryWrite(string path, string text)
    {
        try
        {
            File.WriteAllText(path, text);
        }
        catch (Exception)
        {
            // the caller's default holds
        }
    }

    // A failed lookup or wait is checked again this often, this far apart.
    private const int WaitTries = 50, WaitRetryMs = 2000;

    /// <summary>
    /// Waits until the game process has really ended and says how the wait ended. A failed lookup or wait is checked again, so a
    /// running game is never called crashed (issue keel-crash-without-data: windows came up while the game still ran).
    /// </summary>
    private static string WaitFor(int pid)
    {
        string how = "?";
        for (int tries = 0; tries < WaitTries; tries++)
        {
            try
            {
                using (Process game = Process.GetProcessById(pid))
                {
                    game.WaitForExit();
                    how = "exited";
                }
            }
            catch (ArgumentException)
            {
                how = tries == 0 ? "not found at the start" : "gone";
            }
            catch (Exception e)
            {
                how = "wait failed: " + e.GetType().Name;
            }
            if (!StillRunning(pid))
                return how;
            System.Threading.Thread.Sleep(WaitRetryMs);
        }
        return how + ", still running after retries";
    }

    private static bool StillRunning(int pid)
    {
        try
        {
            using (Process p = Process.GetProcessById(pid))
                return !p.HasExited;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>One line into Keel-watcher.txt: how each wait ended and what the watcher did.</summary>
    private static void Note(string dir, string text)
    {
        try
        {
            File.AppendAllText(Path.Combine(dir, KeelNames.WatcherFile), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\t{text}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // only a trace
        }
    }

    /// <summary>
    /// Keel-crash.txt for the session that just ended, before any bundle is packed: else the bundle carries the text of an older
    /// session (issue keel-bundle-stale-files). Keel writes the same at the next start.
    /// </summary>
    private static void WriteCrashFile(CrashText.Journal journal, string dir, string stack) =>
        TryWrite(Path.Combine(dir, KeelNames.CrashFile),
                 CrashText.CrashFileText(journal, string.IsNullOrWhiteSpace(stack) ? null : stack));

    private static bool Ask(string text, string name, string detail, string dir, bool isPack, out bool keepOff)
    {
        var kit = new DialogKit(460);
        using var form = new Form { Text = Title, TopMost = true, StartPosition = FormStartPosition.CenterScreen };
        kit.Style(form);
        FlowLayoutPanel column = kit.Column();
        column.Controls.Add(kit.Line("Sea Power crashed", DialogKit.Heading, DialogKit.HeadingColor, 8));
        column.Controls.Add(kit.Line(text, form.Font, DialogKit.TextColor, 16));
        CheckBox off = null;
        if (name != null)
        {
            // A pack named only for being loaded last is often the campaign being played and the victim too: off is offered, not ticked.
            off = new CheckBox { Text = $"Keep {name} switched off for now", Checked = !isPack, AutoSize = true, MaximumSize = new Size(kit.Wide, 0), Margin = new Padding(0, 0, 0, kit.P(16)) };
            column.Controls.Add(off);
        }
        if (detail != null)
            column.Controls.Add(kit.Line("Technical detail: " + detail, new Font("Segoe UI", 8.5f), Color.FromArgb(120, 120, 120), 16));

        Button start = kit.Button("Start the game"), close = kit.Button("Close", left: 8);
        start.DialogResult = DialogResult.Yes;
        close.DialogResult = DialogResult.No;
        FlowLayoutPanel row = DialogKit.Row();
        row.Controls.Add(close);
        row.Controls.Add(start);
        row.Controls.Add(SaveLogsButton(kit, dir));
        Button report = kit.Button("Send report", right: 8);
        report.Click += (s, e) => Sender.Run(dir, null, form, crashed: true);
        row.Controls.Add(report);
        column.Controls.Add(row);
        form.Controls.Add(column);
        form.AcceptButton = start;
        form.CancelButton = close;
        form.Shown += (s, e) =>
        {
            form.Activate();
            form.ActiveControl = start;
        };
        DialogResult result = form.ShowDialog();
        keepOff = off == null || off.Checked;
        return result == DialogResult.Yes;
    }

    private static Button SaveLogsButton(DialogKit kit, string dir)
    {
        Button logs = kit.Button("Save logs", right: 8);
        logs.Click += (s, e) =>
        {
            logs.Enabled = false;
            try
            {
                LogBundle.Reveal(LogBundle.Save(dir));
                logs.Text = "Saved to your Desktop";
            }
            catch (Exception)
            {
                logs.Text = "Could not save";
            }
        };
        return logs;
    }

    // The name a pack's _info.ini gives it, else its folder's name.
    private static string FolderName(string fullPath, string folder)
    {
        try
        {
            string p = fullPath.Replace('\\', '/');
            int at = p.IndexOf("/" + folder + "/", StringComparison.OrdinalIgnoreCase);
            string ini = Path.Combine(p.Substring(0, at + folder.Length + 1), "_info.ini");
            string line = File.Exists(ini) ? File.ReadLines(ini).FirstOrDefault(l => l.StartsWith("Name=", StringComparison.OrdinalIgnoreCase)) : null;
            if (line != null && line.Length > 5)
                return line.Substring(5).Trim();
        }
        catch (Exception)
        {
            // the folder's own name will do
        }
        return folder;
    }

    private const string KeepsHappening = "\n\nIf it keeps happening, click Send report to send the logs to the R.A.T.S. developer, or Save logs and attach the file to a bug report.";

    /// <summary>The crash window's text, and the mod (or pack) it names: the same blame as Keel's at the next start (CrashGuard).</summary>
    internal static string Explain(CrashText.Journal journal, string dir, out string guid, out string name, out string detail, out string pack)
    {
        guid = name = pack = null;
        string block = CrashText.ReadAll(Path.Combine(dir, KeelNames.StackFile));
        if (string.IsNullOrWhiteSpace(block))
            block = null;
        detail = CrashText.CrashLine(block);

        // Id, display name and code root of each mod, in the order Keel uses for blame.
        List<string[]> mods = CrashText.ReadLines(Path.Combine(dir, KeelNames.ModsFile)).Select(l => l.Split('\t')).Where(p => p.Length == 3).ToList();
        if (journal.Guid != null)
        {
            string id = journal.Guid;
            guid = id;
            name = mods.FirstOrDefault(m => m[0] == id)?[1] ?? id;
            return $"The game stopped while {name} was {(journal.Step == "load" ? "loading" : "starting")}, so this mod is the cause.";
        }
        int hit = CrashText.FirstHit(block, mods.Select(m => m[2]).ToList());
        if (hit >= 0)
        {
            guid = mods[hit][0];
            name = mods[hit][1];
            return $"{name} was running when the game crashed, so it is the most likely cause.";
        }
        if (CrashText.LastPack(block, out string folder, out string file))
        {
            pack = folder;
            name = FolderName(file, folder);
            return $"The game was loading {Path.GetFileName(file)} from {name} just before it crashed, so this mod is the most likely cause.";
        }
        if (block == null)
            return "Keel found no crash record for this game: the game may have frozen and been ended, or been closed from outside. No mod is to blame from what Keel can see." + CrashText.ActivityHint(journal) + KeepsHappening;
        if (CrashText.InObjectSearch(block))
            return "The game crashed inside Unity's object search. That happens when the game's object list is already damaged, so the code that called the search is not the cause and no mod is held." + KeepsHappening;
        return "Keel couldn't tell which mod caused this. It may be the game itself, or a mod's models or files." + CrashText.ActivityHint(journal) + KeepsHappening;
    }

    // Steam counts this process as part of the game until it ends, so the start comes from a detached shell a few seconds later.
    private const int RestartDelaySeconds = 4;

    private static void Restart(string exe)
    {
        bool steam = exe.IndexOf(@"\steamapps\common\", StringComparison.OrdinalIgnoreCase) >= 0 || exe.IndexOf("/steamapps/common/", StringComparison.OrdinalIgnoreCase) >= 0;
        string start = steam ? "start \"\" steam://rungameid/" + KeelNames.SteamAppId : $"start \"\" /d \"{Path.GetDirectoryName(exe)}\" \"{exe}\"";
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c timeout /t {RestartDelaySeconds} /nobreak >nul & " + start) { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
    }
}
