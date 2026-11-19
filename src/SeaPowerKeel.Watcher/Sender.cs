using System;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SeaPowerKeel;

/// <summary>
/// "Send report": packs the same logs as Save logs, with the Windows user name taken out of every path, shows the player what
/// goes out and sends it to the R.A.T.S. feedback board only after the player clicks Send. Nothing is sent without that click.
/// </summary>
internal static class Sender
{
    private const string Endpoint = "https://hub.sewerlabs.dev/api/report";
    private const string Board = "https://hub.sewerlabs.dev";
    private const long MaxBytes = 5 * 1024 * 1024;     // the board refuses more
    // TLS 1.3: .NET Framework 4.8 runs with it on Windows 11 but has no name for it.
    private const SecurityProtocolType Tls13 = (SecurityProtocolType)12288;

    private static string KeelVersion =>
        typeof(Sender).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "";

    /// <summary>
    /// Shows the send window. <paramref name="game"/> is the game build if the caller knows it; <paramref name="crashed"/> says
    /// whether the game crashed (the crash window) or the player sends from the report window: the board titles its mail by it.
    /// </summary>
    public static void Run(string bepInEx, string game = null, IWin32Window owner = null, bool crashed = false)
    {
        (string build, string summary) = ReportFacts.Read(bepInEx, game);
        game = build;
        kind = crashed ? "crash" : "report";
        Sender.summary = summary;
        string folder = Path.Combine(Path.GetTempPath(), "RATS-Keel-report-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        try
        {
            Directory.CreateDirectory(folder);
            string zip = LogBundle.Save(bepInEx, folder, scrub: true);
            Ask(zip, game, owner);
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, "Keel could not pack the logs: " + ex.Message + "\n\nYou can still use Save logs and attach the file to a post on " + Board + ".",
                            "R.A.T.S. Keel", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception)
            {
                // Windows cleans its temp folder in time
            }
        }
    }

    private static void Ask(string zip, string game, IWin32Window owner)
    {
        using var form = new SendForm(zip, game, owner);
        form.ShowDialog(owner);
    }

    private static readonly Color Bad = Color.FromArgb(170, 40, 30), Good = Color.FromArgb(30, 120, 60);

    /// <summary>The send window: what goes out, a note from the player, Send and Cancel, and how the sending went.</summary>
    private sealed class SendForm : Form
    {
        private readonly string zip, game;
        private readonly TextBox note;
        private readonly Label status;
        private readonly Button send, cancel;
        private bool sent;

        public SendForm(string zip, string game, IWin32Window owner)
        {
            this.zip = zip;
            this.game = game;
            var kit = new DialogKit(480);
            Text = "R.A.T.S. Keel";
            TopMost = true;
            StartPosition = owner == null ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
            ShowInTaskbar = owner == null;
            kit.Style(this);
            long size = new FileInfo(zip).Length;
            FlowLayoutPanel column = kit.Column();
            column.Controls.Add(kit.Line("Send logs to the R.A.T.S. developer?", DialogKit.Heading, DialogKit.HeadingColor, 8));
            column.Controls.Add(kit.Line(
                "These files go to the R.A.T.S. feedback board (hub.sewerlabs.dev), where only the mod's developer can open them. " +
                "They are the logs of Keel, BepInEx and the game: which mods ran, errors, and your graphics card, Windows version and game settings as the game logs them. " +
                "Your Windows user name is replaced with %USERNAME% in every path. Nothing else is sent, and nothing is sent unless you click Send.",
                Font, DialogKit.TextColor, 10));
            column.Controls.Add(FileList(kit, zip));
            var look = new LinkLabel { Text = $"Show the zip ({Size(size)}) in Explorer", AutoSize = true, Margin = new Padding(0, 0, 0, kit.P(12)) };
            look.LinkClicked += (s, e) => LogBundle.Reveal(zip);
            column.Controls.Add(look);
            column.Controls.Add(kit.Line("What happened? (optional)", Font, DialogKit.TextColor, 4));
            note = new TextBox { Multiline = true, Width = kit.Wide, Height = kit.P(64), MaxLength = 1000, ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 0, 0, kit.P(14)) };
            column.Controls.Add(note);
            status = kit.Line("", Font, DialogKit.TextColor, 12);
            status.Visible = false;
            column.Controls.Add(status);

            send = kit.Button("Send");
            cancel = kit.Button("Cancel", left: 8);
            cancel.DialogResult = DialogResult.Cancel;
            FlowLayoutPanel row = DialogKit.Row();
            row.Controls.Add(cancel);
            row.Controls.Add(send);
            column.Controls.Add(row);
            Controls.Add(column);
            CancelButton = cancel;
            if (size > MaxBytes)
            {
                send.Enabled = false;
                Say($"The logs are {Size(size)}, more than the 5 MB the board takes. Please use Save logs and attach the file to a post on {Board}.", Bad);
            }
            send.Click += async (s, e) => await SendClicked();
            Shown += (s, e) =>
            {
                Activate();
                ActiveControl = note;
            };
        }

        private static ListBox FileList(DialogKit kit, string zip)
        {
            var list = new ListBox
            {
                Width = kit.Wide, Height = kit.P(110), IntegralHeight = false, Font = new Font("Consolas", 9f), Margin = new Padding(0, 0, 0, kit.P(4)),
                HorizontalScrollbar = true, SelectionMode = SelectionMode.None,
            };
            using (ZipArchive archive = ZipFile.OpenRead(zip))
                list.Items.AddRange(archive.Entries.Select(e => (object)$"{e.FullName}   ({Size(e.Length)})").ToArray());
            return list;
        }

        private void Say(string text, Color color)
        {
            status.Text = text;
            status.ForeColor = color;
            status.Visible = true;
        }

        private async Task SendClicked()
        {
            if (sent)
            {
                Close();
                return;
            }
            send.Enabled = cancel.Enabled = note.Enabled = false;
            Say("Sending …", DialogKit.TextColor);
            string answer = await Task.Run(() => Upload(zip, note.Text.Trim(), game));
            if (answer.StartsWith("#", StringComparison.Ordinal))
            {
                sent = true;
                Say($"Sent, thank you. Your report is number {answer}. If you post about it on {Board}, mention that number.", Good);
                send.Text = "Close";
                send.Enabled = true;
                AcceptButton = send;
                return;
            }
            Say(answer + $"\n\nYou can still use Save logs and attach the file to a post on {Board}.", Bad);
            send.Text = "Try again";
            send.Enabled = cancel.Enabled = note.Enabled = true;
        }
    }

    // What the board's mail says: a crash or a report sent from the report window, and the report's one-line summary.
    private static string kind = "report", summary = "";

    /// <summary>"#id" when the board took the report, else a sentence for the player.</summary>
    private static string Upload(string zip, string note, string game)
    {
        try
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | Tls13;
            }
            catch (NotSupportedException)
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"RATS-Keel/{(KeelVersion.Length > 0 ? KeelVersion : "0")}");
            using var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(File.ReadAllBytes(zip));
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");
            form.Add(file, "logs", Path.GetFileName(zip));
            form.Add(new StringContent(note ?? ""), "note");
            form.Add(new StringContent(KeelVersion), "keel");
            form.Add(new StringContent(game ?? ""), "game");
            form.Add(new StringContent(kind), "kind");          // crash | report: the board's mail says "Crash report" only for a crash
            form.Add(new StringContent(summary ?? ""), "summary");
            using HttpResponseMessage response = http.PostAsync(Endpoint, form).GetAwaiter().GetResult();
            string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            Match id = Regex.Match(body, "\"id\"\\s*:\\s*(\\d+)");
            if (response.IsSuccessStatusCode && id.Success)
                return "#" + id.Groups[1].Value;
            Match error = Regex.Match(body, "\"error\"\\s*:\\s*\"([^\"]*)\"");
            return error.Success ? "The board did not take the report: " + error.Groups[1].Value : $"The board did not take the report (HTTP {(int)response.StatusCode}).";
        }
        catch (Exception ex)
        {
            return "Could not reach the board: " + (ex.InnerException?.Message ?? ex.Message);
        }
    }

    private static string Size(long bytes) => bytes >= 1024 * 1024 ? $"{bytes / 1024.0 / 1024.0:0.0} MB" : $"{Math.Max(1, bytes / 1024)} KB";
}
