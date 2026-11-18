using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SeaPowerKeel;

/// <summary>
/// KeelSetup.exe: one window that gets Keel running. It carries no other program inside: it copies the files that lie next to it in
/// Keel's folder (the starter DLL, the takeover patcher) and unpacks BepInEx from its official release zip, also next to it.
/// </summary>
internal static class Program
{
    [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

    [STAThread]
    private static void Main()
    {
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.Run(new SetupForm());
    }

    private static string Here => AppDomain.CurrentDomain.BaseDirectory;
    public static string StubFile => Path.Combine(Here, "copy-to-BepInEx-plugins", "SeaPowerKeel.Stub.dll");
    public static string PatcherFile => Path.Combine(Here, "SeaPowerKeel.Patcher.dll");

    /// <summary>BepInEx's official release zip in Keel's folder, or null.</summary>
    public static string BepInExZip
    {
        get
        {
            try
            {
                return Directory.GetFiles(Here, "BepInEx_win_x64_*.zip").OrderBy(f => f).LastOrDefault();
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>The SHA-256 of the official zip this build was made with (set at build time; empty in a developer build).</summary>
    private static string ExpectedZipHash =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "BepInExSha256")?.Value ?? "";

    /// <summary>What is missing or wrong in Keel's folder, or null when the setup has everything it copies.</summary>
    public static string PayloadProblem(bool needBepInEx)
    {
        var missing = new List<string>();
        if (!File.Exists(StubFile))
            missing.Add(@"copy-to-BepInEx-plugins\SeaPowerKeel.Stub.dll");
        if (!File.Exists(PatcherFile))
            missing.Add("SeaPowerKeel.Patcher.dll");
        string zip = BepInExZip;
        if (needBepInEx && zip == null)
            missing.Add("BepInEx_win_x64_*.zip");
        if (missing.Count > 0)
            return "Run KeelSetup.exe from Keel's own folder. Missing next to it: " + string.Join(", ", missing) + ".";
        if (needBepInEx && ExpectedZipHash.Length > 0 && !string.Equals(Sha256(zip), ExpectedZipHash, StringComparison.OrdinalIgnoreCase))
            return Path.GetFileName(zip) + " is not the official BepInEx release this setup was built with. Let Steam verify Keel's files, or download Keel again.";
        return null;
    }

    private static string Sha256(string file)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using FileStream stream = File.OpenRead(file);
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
    }

    /// <summary>Sea Power in any Steam library on this PC, or null.</summary>
    public static string FindGame()
    {
        try
        {
            string steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
            if (steam == null)
                return null;
            var libraries = new List<string> { steam };
            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (File.Exists(vdf))
                libraries.AddRange(Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"(.+?)\"").Cast<Match>().Select(m => m.Groups[1].Value.Replace(@"\\", @"\")));
            return libraries.Select(l => Path.Combine(l, "steamapps", "common", "Sea Power")).FirstOrDefault(p => File.Exists(Path.Combine(p, SetupCore.ExeName)));
        }
        catch (Exception)
        {
            return null;
        }
    }
}

internal sealed class SetupForm : Form
{
    private readonly TextBox folder;
    private readonly Label status;
    private readonly Button install, restore;
    private readonly int wide;

    public SetupForm()
    {
        float dpi;
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            dpi = g.DpiX;
        int P(int v) => (int)Math.Round(v * dpi / 96f);
        wide = P(500);

        Text = "R.A.T.S. Keel Setup";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 10f);
        Padding = new Padding(0, 0, P(26), P(20));

        var column = new FlowLayoutPanel { Location = new Point(P(26), P(22)), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
        Label Line(string s, Font f, Color c, int below) => new() { Text = s, Font = f, ForeColor = c, AutoSize = true, MinimumSize = new Size(wide, 0), MaximumSize = new Size(wide, 0), Margin = new Padding(0, 0, 0, P(below)) };
        column.Controls.Add(Line("R.A.T.S. Keel", new Font("Segoe UI Semibold", 15f), Color.FromArgb(32, 32, 32), 4));
        column.Controls.Add(Line("Sets up Keel for your copy of Sea Power. You can undo it here at any time.", Font, Color.FromArgb(70, 70, 70), 16));

        var pick = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, P(12)) };
        folder = new TextBox { Text = Program.FindGame() ?? "", Width = wide - P(100), Margin = new Padding(0, P(2), P(8), 0) };
        var browse = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(0) };
        browse.Click += (s, e) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Sea Power|" + SetupCore.ExeName, Title = "Find Sea Power.exe" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                folder.Text = Path.GetDirectoryName(dialog.FileName);
        };
        folder.TextChanged += (s, e) => Refresh2();
        pick.Controls.Add(folder);
        pick.Controls.Add(browse);
        column.Controls.Add(pick);

        status = Line("", Font, Color.FromArgb(48, 48, 48), 16);
        column.Controls.Add(status);

        var row = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.Cancel, Padding = new Padding(P(10), P(2), P(10), P(2)), Margin = new Padding(P(8), 0, 0, 0) };
        install = new Button { Text = "Install", AutoSize = true, Padding = new Padding(P(10), P(2), P(10), P(2)), Margin = new Padding(0) };
        restore = new Button { Text = "Restore", AutoSize = true, Padding = new Padding(P(10), P(2), P(10), P(2)), Margin = new Padding(0, 0, P(8), 0) };
        install.Click += (s, e) => Do(c =>
        {
            string problem = Program.PayloadProblem(!c.HasBepInEx);
            if (problem != null)
                return problem;
            byte[] zip = c.HasBepInEx ? new byte[0] : File.ReadAllBytes(Program.BepInExZip);
            return "Done. " + string.Join(" ", c.Install(File.ReadAllBytes(Program.StubFile), zip, File.ReadAllBytes(Program.PatcherFile))) +
                   "\n\nStart Sea Power, switch R.A.T.S. Keel on in Mods, and restart the game.";
        });
        restore.Click += (s, e) => Do(c => { c.Restore(); return "Everything Keel's setup changed is back as it was."; });
        close.Click += (s, e) => Close();
        row.Controls.Add(close);
        row.Controls.Add(install);
        row.Controls.Add(restore);
        column.Controls.Add(row);
        Controls.Add(column);
        CancelButton = close;
        AcceptButton = install;
        Refresh2();
    }

    private static bool GameRunning => Process.GetProcessesByName("Sea Power").Length > 0;

    private void Do(Func<SetupCore, string> action)
    {
        if (GameRunning)
        {
            status.Text = "Sea Power is running. Close it first.";
            return;
        }
        string result;
        try
        {
            result = action(new SetupCore(folder.Text.Trim()));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            result = "That did not work: " + ex.Message;
        }
        Refresh2();
        status.Text = result;
    }

    private void Refresh2()
    {
        var core = new SetupCore(folder.Text.Trim());
        bool ok = folder.Text.Trim().Length > 0 && core.IsGameFolder;
        install.Enabled = ok;
        restore.Enabled = ok && core.CanRestore;
        if (!ok)
        {
            status.Text = "Choose the folder that holds Sea Power.exe.";
            return;
        }
        string problem = Program.PayloadProblem(!core.HasBepInEx);
        if (problem != null)
        {
            install.Enabled = false;
            status.Text = problem;
            return;
        }
        var lines = new List<string> { core.HasStub ? "Keel is set up in this folder. Install again to repair it." : "Ready to install." };
        if (!core.HasBepInEx)
            lines.Add("BepInEx is missing and will be added from its official release (" + Path.GetFileName(Program.BepInExZip) + ").");
        lines.Add("Install only copies files into this game folder. No administrator rights, no internet access.");
        if (core.HasOtherPreloader)
            lines.Add("Another loader's preloader will be set aside, so only one loader is active. Restore brings it back.");
        if (core.HasAlpine)
            lines.Add(core.HasOtherPreloader ? "Alpine's Mod Loader is on this PC. Keel will run your mods instead; Restore switches back." : "Alpine's Mod Loader is on this PC, but it is not active in this game.");
        status.Text = string.Join("\n", lines);
    }
}
