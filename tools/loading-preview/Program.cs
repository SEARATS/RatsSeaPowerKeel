using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SeaPowerKeel.Loading;

internal static class Program
{
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFO bi, uint usage);

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPels, biYPels, biClrUsed, biClrImportant;
    }

    private static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "live") return Live();
        if (args.Length < 2) { Console.WriteLine("usage: preview <Keel-log.txt> <out folder> [scale]  |  preview live"); return 1; }
        float scale = args.Length > 2 ? float.Parse(args[2], CultureInfo.InvariantCulture) : 1f;
        Directory.CreateDirectory(args[1]);

        // "21:14:45.141 +41.826s [Info    : Keel] RUNNING: ..."
        var line = new Regex(@"^\d\d:\d\d:\d\d\.\d+ \+(\d+\.\d+)s \[(\w+)\s*: (.+?)\] (.*)$");
        double now = 0;
        var progress = new Progress(() => now);
        var events = new List<(double t, string src, string text)>();
        foreach (string raw in File.ReadLines(args[0]))
        {
            Match m = line.Match(raw);
            if (!m.Success) continue;
            events.Add((double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[3].Value, m.Groups[4].Value));
            if (events.Count > 4000) break;
        }

        // replay: draw at chosen moments (seconds into the game process)
        double[] shots = { 5, 12, 20, 33, 45, 58, 70, 80, 88.8, 92 };
        int next = 0;
        foreach (var ev in events)
        {
            while (next < shots.Length && shots[next] < ev.t)
            {
                now = shots[next];
                Render(progress, scale, Path.Combine(args[1], "t" + ((int)shots[next]).ToString("000") + ".bmp"));
                next++;
            }
            now = ev.t;
            if (ev.src == KeelSource) KeelLine(progress, ev.text);
            else progress.Feed(ev.src, ev.text);
        }
        while (next < shots.Length)
        {
            now = shots[next];
            Render(progress, scale, Path.Combine(args[1], "t" + ((int)shots[next]).ToString("000") + ".bmp"));
            next++;
        }
        Render(progress, scale, Path.Combine(args[1], "summary.bmp"), summary: true);
        Console.WriteLine("events replayed: " + events.Count);
        return 0;
    }

    private const string KeelSource = "Keel";
    private static readonly Regex Result = new(@"^([A-Z][A-Z ]+): (.+?) \(");
    private static readonly Regex Starting = new(@"^STARTING (\d+)/(\d+): (.+?) \(");
    private static readonly Regex Found = new(@"^Found (\d+) plugin");
    private static readonly Regex Started = new(@"^Started \d+ plugin\(s\) in (\d+) ms");
    private static readonly Regex Hello = new(@"^R\.A\.T\.S\. Keel (\d+\.\d+\.\d+)");

    // In the game Keel calls the progress model itself; a saved log has only the text of those calls, so the replay reads it back.
    private static void KeelLine(Progress progress, string line)
    {
        Match m;
        if ((m = Starting.Match(line)).Success) progress.Starting(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Value);
        else if ((m = Found.Match(line)).Success) progress.Found(int.Parse(m.Groups[1].Value));
        else if ((m = Started.Match(line)).Success) progress.StartedIn(double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture));
        else if ((m = Hello.Match(line)).Success) progress.KeelStarted(m.Groups[1].Value, line);
        else if (line.StartsWith("Done:")) progress.Done(line);
        else if (line.StartsWith("Start-up load screen done")) progress.LoadScreenDone(line);
        else if (line.StartsWith("The game's mod list is there")) progress.ModListThere(line);
        else if (line.Contains(" mod folders are on")) progress.FoldersOn(line);
        else if ((m = Result.Match(line)).Success && ReportFormat.TryParseTag(m.Groups[1].Value, out PluginState state)) progress.Result(m.Groups[2].Value, state);
    }

    /// <summary>Smoke test of the real window thread: opens it far off every monitor, feeds a few lines, ends it with Keel's "Done" line.</summary>
    private static int Live()
    {
        var start = DateTime.Now;
        var progress = new Progress(() => (DateTime.Now - start).TotalSeconds);
        var overlay = new Overlay(progress, new OverlayOptions { Offscreen = true, LingerSeconds = 0.5, SummarySeconds = 1 });
        string warning = null;
        overlay.Start(m => warning = m);
        for (int i = 0; i < 50 && !overlay.Opened && warning == null; i++) System.Threading.Thread.Sleep(100);
        Console.WriteLine("opened: " + overlay.Opened + (warning != null ? "  warning: " + warning : ""));
        progress.Found(3);
        progress.Result("Test Mod 1.0", PluginState.Running);
        System.Threading.Thread.Sleep(1500);
        progress.Done("Done: 3 running. Report: x");
        for (int i = 0; i < 100 && !overlay.Closed; i++) System.Threading.Thread.Sleep(100);     // linger, move to the corner, summary, fade
        Console.WriteLine("moved to the corner, faded and closed by itself after Done: " + overlay.Closed);
        return overlay.Opened && overlay.Closed ? 0 : 2;
    }

    // KEEL_ALERT: to see the card as it looks after a crash.
    private static readonly string Alert = Environment.GetEnvironmentVariable("KEEL_ALERT");

    private static void Render(Progress progress, float scale, string path, bool summary = false)
    {
        int w = (int)((summary ? Overlay.Layout.SummaryWidth : Overlay.Layout.Width) * scale);
        int h = (int)((summary ? Overlay.Layout.SummaryHeight : Overlay.Layout.Height(Overlay.Layout.Rows)) * scale);
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        IntPtr mem = Native.CreateCompatibleDC(screen);
        IntPtr bmp = Native.CreateCompatibleBitmap(screen, w, h);
        IntPtr old = Native.SelectObject(mem, bmp);
        IntPtr f1 = Overlay.Font(Overlay.Layout.TextPoints, 400, scale), f3 = Overlay.Font(Overlay.Layout.SmallPoints, 400, scale);
        if (summary) Overlay.DrawSummary(mem, w, h, progress.Take(Overlay.Layout.Rows), scale, f3, Alert);
        else Overlay.Draw(mem, w, h, progress.Take(Overlay.Layout.Rows), scale, f1, f3, null);

        var bi = new BITMAPINFO { biSize = Marshal.SizeOf(typeof(BITMAPINFO)), biWidth = w, biHeight = h, biPlanes = 1, biBitCount = 32 };
        var bits = new byte[w * h * 4];
        Native.SelectObject(mem, old);
        GetDIBits(mem, bmp, 0, (uint)h, bits, ref bi, 0);

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((byte)'B'); bw.Write((byte)'M'); bw.Write(54 + bits.Length); bw.Write(0); bw.Write(54);
        bw.Write(40); bw.Write(w); bw.Write(h); bw.Write((short)1); bw.Write((short)32); bw.Write(0); bw.Write(bits.Length); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
        bw.Write(bits);

        foreach (IntPtr f in new[] { f1, f3 }) Native.DeleteObject(f);
        Native.DeleteObject(bmp);
        Native.DeleteDC(mem);
        Native.ReleaseDC(IntPtr.Zero, screen);
    }
}

// The game-side switches the overlay window reads while it runs (the live smoke test); the preview has no game.
#pragma warning disable CS0649 // set by the game in Keel; here they keep their defaults
internal static class LoadingWindow
{
    public static bool MissionRunning, OnMenuSubPage;
    public static long SceneLoadedTicks;
    public static string Alert, WaitingFor;
    public static bool SummaryReady, GameDrawsSummary, OverlayGone;
    public static int SummaryX, SummaryY, SummaryW, SummaryH;
    public static float SummaryScale;
}
