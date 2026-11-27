using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SeaPowerKeel;

/// <summary>
/// KeelSetup.exe: one window that gets Keel running. It carries no other program inside: it copies the files that lie next to it in
/// Keel's folder (the starter DLL, the takeover patcher). BepInEx is not shipped: when the game has none, the setup downloads BepInEx's
/// official release from its GitHub page and checks it against the pinned hash before it unpacks anything.
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
    private const string PluginsCopyFolder = "copy-to-BepInEx-plugins";
    public static string StubFile => Path.Combine(Here, PluginsCopyFolder, KeelNames.StubFile);
    public static string PatcherFile => Path.Combine(Here, KeelNames.PatcherFile);

    private static string Metadata(string key) =>
        Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == key)?.Value ?? "";

    /// <summary>The BepInEx release this build pins, and its SHA-256 (both set at build time by tools\bepinex.ps1; empty in a developer build).</summary>
    public static string BepInExVersion => Metadata("BepInExVersion");
    private static string ExpectedZipHash => Metadata("BepInExSha256");
    public static string BepInExZipName => "BepInEx_win_x64_" + BepInExVersion + ".zip";
    public static string BepInExUrl => "https://github.com/BepInEx/BepInEx/releases/download/v" + BepInExVersion + "/" + BepInExZipName;

    /// <summary>What is missing or wrong in Keel's folder, or null when the setup has everything it copies.</summary>
    public static string PayloadProblem(bool needBepInEx)
    {
        var missing = new List<string>();
        if (!File.Exists(StubFile))
            missing.Add(Path.Combine(PluginsCopyFolder, KeelNames.StubFile));
        if (!File.Exists(PatcherFile))
            missing.Add(KeelNames.PatcherFile);
        if (missing.Count > 0)
            return "Run KeelSetup.exe from Keel's own folder. Missing next to it: " + string.Join(", ", missing) + ".";
        if (needBepInEx && (BepInExVersion.Length == 0 || ExpectedZipHash.Length == 0))
            return "This setup was built without the BepInEx release it may download. Use the KeelSetup.exe from Keel's Workshop folder or its GitHub release.";
        return null;
    }

    /// <summary>
    /// The official BepInEx release zip, checked against the pinned hash: a copy the player put next to KeelSetup.exe (for PCs without
    /// internet), else the download from BepInEx's GitHub release. Keel ships no BepInEx itself. Throws IOException with a player text.
    /// </summary>
    public static byte[] GetBepInExZip()
    {
        string local = Path.Combine(Here, BepInExZipName);
        if (File.Exists(local))
        {
            byte[] bytes = File.ReadAllBytes(local);
            if (HashMatches(bytes))
                return bytes;
            throw new IOException(BepInExZipName + " next to KeelSetup.exe is not the official BepInEx release. Delete it, and Install downloads the right one.");
        }
        byte[] data;
        try
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(90) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("KeelSetup/" + Assembly.GetExecutingAssembly().GetName().Version);
            data = http.GetByteArrayAsync(BepInExUrl).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException || ex is WebException)
        {
            throw new IOException("BepInEx could not be downloaded from GitHub (" + Inner(ex) + "). Check your internet connection and try again. " +
                                  "Without internet: download " + BepInExZipName + " from github.com/BepInEx/BepInEx/releases on another PC and put it next to KeelSetup.exe.");
        }
        if (!HashMatches(data))
            throw new IOException("The BepInEx download is not the official " + BepInExVersion + " release (wrong checksum), so nothing was installed. Try again later.");
        return data;
    }

    private static string Inner(Exception ex)
    {
        while (ex.InnerException != null)
            ex = ex.InnerException;
        return ex is TaskCanceledException ? "no answer in time" : ex.Message.TrimEnd('.');
    }

    private static bool HashMatches(byte[] data)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        string hash = BitConverter.ToString(sha.ComputeHash(data)).Replace("-", "");
        return ExpectedZipHash.Length > 0 && string.Equals(hash, ExpectedZipHash, StringComparison.OrdinalIgnoreCase);
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
    private readonly Button install, restore, browse;
    private readonly int wide;

    public SetupForm()
    {
        var kit = new DialogKit(500);
        wide = kit.Wide;
        Text = "Keel Setup";
        StartPosition = FormStartPosition.CenterScreen;
        kit.Style(this);

        FlowLayoutPanel column = kit.Column();
        column.Controls.Add(kit.Line("Keel", DialogKit.Heading, DialogKit.HeadingColor, 4));
        column.Controls.Add(kit.Line("Sets up Keel for your copy of Sea Power. You can undo it here at any time.", Font, Color.FromArgb(70, 70, 70), 16));
        column.Controls.Add(FolderPicker(kit, out folder, out browse));
        status = kit.Line("", Font, DialogKit.TextColor, 16);
        column.Controls.Add(status);

        Button close = kit.Button("Close", left: 8);
        close.DialogResult = DialogResult.Cancel;
        install = kit.Button("Install");
        restore = kit.Button("Restore", right: 8);
        install.Click += async (s, e) => await InstallClicked();
        restore.Click += (s, e) => Do(c => { c.Restore(); return "Everything Keel's setup changed is back as it was."; });
        close.Click += (s, e) => Close();
        FlowLayoutPanel row = DialogKit.Row();
        row.Controls.Add(close);
        row.Controls.Add(install);
        row.Controls.Add(restore);
        column.Controls.Add(row);
        Controls.Add(column);
        CancelButton = close;
        AcceptButton = install;
        Refresh2();
    }

    private FlowLayoutPanel FolderPicker(DialogKit kit, out TextBox box, out Button pickButton)
    {
        var pick = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 0, kit.P(12)) };
        TextBox path = box = new TextBox { Text = Program.FindGame() ?? "", Width = wide - kit.P(100), Margin = new Padding(0, kit.P(2), kit.P(8), 0) };
        pickButton = new Button { Text = "Browse...", AutoSize = true, Margin = new Padding(0) };
        pickButton.Click += (s, e) =>
        {
            using var dialog = new OpenFileDialog { Filter = "Sea Power|" + SetupCore.ExeName, Title = "Find Sea Power.exe" };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                path.Text = Path.GetDirectoryName(dialog.FileName);
        };
        path.TextChanged += (s, e) => Refresh2();
        pick.Controls.Add(path);
        pick.Controls.Add(pickButton);
        return pick;
    }

    // Install: BepInEx downloaded first when the game has none (off the window's thread), then the files copied.
    private async Task InstallClicked()
    {
        var core = new SetupCore(folder.Text.Trim());
        byte[] zip = new byte[0];
        if (!GameRunning && core.IsGameFolder && !core.HasBepInEx && Program.PayloadProblem(true) == null)
        {
            install.Enabled = restore.Enabled = browse.Enabled = folder.Enabled = false;
            status.Text = "Downloading BepInEx " + Program.BepInExVersion + " from its GitHub release...";
            try
            {
                zip = await Task.Run(() => Program.GetBepInExZip());
            }
            catch (IOException ex)
            {
                status.Text = ex.Message;
                browse.Enabled = folder.Enabled = true;
                Refresh2(keepStatus: true);
                return;
            }
            browse.Enabled = folder.Enabled = true;
        }
        Do(c =>
        {
            string problem = Program.PayloadProblem(!c.HasBepInEx);
            if (problem != null)
                return problem;
            if (!c.HasBepInEx && zip.Length == 0)
                return "BepInEx is missing. Press Install again to download it.";
            return "Done. " + string.Join(" ", c.Install(File.ReadAllBytes(Program.StubFile), c.HasBepInEx ? new byte[0] : zip, File.ReadAllBytes(Program.PatcherFile))) +
                   "\n\nStart Sea Power, switch Keel on in Mods, and restart the game.";
        });
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

    private void Refresh2(bool keepStatus = false)
    {
        var core = new SetupCore(folder.Text.Trim());
        bool ok = folder.Text.Trim().Length > 0 && core.IsGameFolder;
        install.Enabled = ok;
        restore.Enabled = ok && core.CanRestore;
        if (keepStatus)
            return;
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
            lines.Add("BepInEx is missing: Install downloads the official BepInEx " + Program.BepInExVersion + " from its GitHub release and checks it before it adds it.");
        lines.Add(core.HasBepInEx
            ? "Install only copies files into this game folder. No administrator rights, no internet access."
            : "Install only copies files into this game folder. No administrator rights; internet only for that one download.");
        if (core.HasOtherPreloader)
            lines.Add("Another loader's preloader will be set aside, so only one loader is active. Restore brings it back.");
        if (core.HasAlpine)
            lines.Add(core.HasOtherPreloader ? "Alpine's Mod Loader is on this PC. Keel will run your mods instead; Restore switches back." : "Alpine's Mod Loader is on this PC, but it is not active in this game.");
        status.Text = string.Join("\n", lines);
    }
}
