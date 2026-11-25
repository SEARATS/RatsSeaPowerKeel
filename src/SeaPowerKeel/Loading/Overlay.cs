using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace SeaPowerKeel.Loading;

/// <summary>Where the window sits while the game starts. Marker = the blank band under the main menu's news box (the spot the author picked).</summary>
internal enum Spot { Marker, TopLeft, TopRight, BottomLeft, BottomRight }

internal sealed class OverlayOptions
{
    public Spot Spot = Spot.TopRight;
    public float Opacity = 0.92f;
    public float Scale;                    // 0 = by the game window's height
    public double MaxSeconds = 300;        // the loading window is gone (or a summary) this long after the game process started, whatever happens
    public double LingerSeconds = 2.0;     // how long the full window stays once the mods are all started, before it moves to the corner
    public double SummarySeconds = 0;      // 0 = the summary stays until clicked away (Keel always; >0 only for the preview tool)
    public bool ClickToHide = true;
    public bool Offscreen;                 // tests: the window is made far off every monitor
    public Action OnSummaryClick;          // a click on the summary (Keel opens its report)
}

/// <summary>
/// The loading window: a never-activated Win32 popup on its own thread. The game's main thread (and with it Unity's GUI) stands still while
/// a slow mod starts, so only a window of our own can keep counting. It is OWNED by the game's window: it stays above the game but not
/// above other programs, and goes when the game is minimized. Phases: the full window on the marker; when every mod has started it shrinks
/// and glides to the bottom left, above the game's version text, as a summary that stays until clicked away.
/// </summary>
internal sealed class Overlay
{
    private const string ClassName = "RatsLoadingOverlay";
    private const int Tick = 16;
    private const double MoveSeconds = 0.8, FadeSeconds = 0.8;
    private static Native.WndProc proc;                     // kept alive: Win32 holds only the pointer
    private static Native.EnumWindowsProc enumProc;

    private enum Stage { Full, Moving, Summary, Away, Fading, HandOff }

    private const double HandOffSeconds = 0.25;

    private readonly Progress progress;
    private readonly OverlayOptions options;
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private IntPtr hwnd, owner;
    private float scale = 1f;
    private IntPtr fontText, fontSmall;
    private Native.RECT game;                                 // the game's window (or the main monitor until it exists)
    private Stage stage = Stage.Full;
    private double stageStart;
    private Native.RECT from, to, now;                        // the rectangles of the move
    private IntPtr snapDc, snapBmp, snapOld;                  // the full window as it was when the move began
    private int snapW, snapH;
    private Progress.Snapshot summary;
    public volatile bool Opened, Closed;

    public Overlay(Progress progress, OverlayOptions options)
    {
        this.progress = progress;
        this.options = options;
    }

    public void Start(Action<string> warn)
    {
        var thread = new Thread(() =>
        {
            try { Run(); }
            catch (Exception ex) { warn?.Invoke("Loading window stopped: " + ex.Message); }
        }) { IsBackground = true, Name = "R.A.T.S. Loading window" };
        thread.Start();
    }

    private double Now => clock.Elapsed.TotalSeconds;

    // ---- window ---------------------------------------------------------------------------------------------------------

    private void Run()
    {
        IntPtr instance = Native.GetModuleHandleW(null);
        proc = WndProc;
        var cls = new Native.WNDCLASSEXW
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.WNDCLASSEXW)),
            lpfnWndProc = System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(proc),
            hInstance = instance,
            hCursor = Native.LoadCursorW(IntPtr.Zero, new IntPtr(32512)),   // IDC_ARROW
            lpszClassName = ClassName,
        };
        Native.RegisterClassExW(ref cls);                                      // already registered (a second start): fine

        MeasureGame();
        ApplyScale();
        Native.RECT full = FullRect();
        if (options.Offscreen) full = new Native.RECT { Left = -32000, Top = -32000, Right = -32000 + (full.Right - full.Left), Bottom = -32000 + (full.Bottom - full.Top) };
        now = full;

        // Not topmost: that put it above every program. The game's window becomes its owner as soon as that exists (see FindOwner).
        hwnd = Native.CreateWindowExW(Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE | Native.WS_EX_LAYERED,
            ClassName, "R.A.T.S. Keel Loading", Native.WS_POPUP, full.Left, full.Top, full.Right - full.Left, full.Bottom - full.Top,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("CreateWindow failed");
        SetAlpha(options.Opacity);
        Native.SetTimer(hwnd, new UIntPtr(1), Tick, IntPtr.Zero);
        Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
        Paint();
        Opened = true;

        while (Native.GetMessageW(out Native.MSG msg, IntPtr.Zero, 0, 0) > 0)
        {
            Native.TranslateMessage(ref msg);
            Native.DispatchMessageW(ref msg);
        }
        FreeSnapshot();
        foreach (IntPtr f in new[] { fontText, fontSmall })
            if (f != IntPtr.Zero) Native.DeleteObject(f);
        Closed = true;
        LoadingWindow.OverlayGone = true;
    }

    private void SetAlpha(float a) =>
        Native.SetLayeredWindowAttributes(hwnd, 0, (byte)(Math.Max(0f, Math.Min(1f, a)) * 255), Native.LWA_ALPHA);

    private int ticks;

    private IntPtr WndProc(IntPtr h, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case Native.WM_ERASEBKGND:
                return new IntPtr(1);
            case Native.WM_MOUSEACTIVATE:
                return new IntPtr(3);                                         // MA_NOACTIVATE: a click never takes focus from the game
            case Native.WM_PAINT:
                Paint();
                Native.ValidateRect(h, IntPtr.Zero);
                return IntPtr.Zero;
            case Native.WM_LBUTTONDOWN:
                OnClick((short)((long)lParam & 0xFFFF), (short)(((long)lParam >> 16) & 0xFFFF));
                return IntPtr.Zero;
            case Native.WM_RBUTTONDOWN:
                if (options.ClickToHide) Begin(Stage.Fading);
                return IntPtr.Zero;
            case Native.WM_TIMER:
                try { OnTick(); }
                catch (Exception) { Close(); }
                return IntPtr.Zero;
            case Native.WM_DESTROY:
                Native.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return Native.DefWindowProcW(h, msg, wParam, lParam);
    }

    // A click on the summary opens Keel's report, on its credit the author's profile; on the full window it hides it.
    private void OnClick(int x, int y)
    {
        if (stage == Stage.Summary)
        {
            if (Layout.CreditSpot(scale).Contains(x, y))
            {
                try { Process.Start(new ProcessStartInfo(Layout.ProfileUrl) { UseShellExecute = true }); } catch (Exception) { /* no browser: nothing to do */ }
            }
            else options.OnSummaryClick?.Invoke();
        }
        else if (options.ClickToHide && stage == Stage.Full) Begin(Stage.Fading);
    }

    private void Begin(Stage next)
    {
        stage = next;
        stageStart = Now;
        if (next == Stage.Moving)
        {
            summaryStartTicks = DateTime.UtcNow.Ticks;
            summary = progress.Take(0);
            TakeSnapshot();
            from = now;
            to = SummaryRect();
        }
    }

    private long summaryStartTicks;

    private void OnTick()
    {
        // The summary card belongs to the main menu: when a mission runs or the game loads a scene of its own (mission, editor,
        // campaign), it goes.
        if ((stage == Stage.Moving || stage == Stage.Summary || stage == Stage.Away) && (LoadingWindow.MissionRunning || LoadingWindow.SceneLoadedTicks > summaryStartTicks))
            Begin(Stage.Fading);
        if (owner == IntPtr.Zero && ++ticks % 6 == 1) FindOwner();
        double t = Now - stageStart;
        bool paint = stage switch
        {
            Stage.Full => TickFull(),
            Stage.Moving => TickMoving(t),
            Stage.Summary => TickSummary(t),
            Stage.Away => TickAway(),
            Stage.HandOff => TickFade(t, HandOffSeconds),
            _ => TickFade(t, FadeSeconds),
        };
        if (paint) Paint();
    }

    // Each stage's step; false when the window is gone or hidden (nothing to paint).

    private bool handedOffFull;

    private bool TickFull()
    {
        Progress.Snapshot s = progress.Take(0);
        if (LoadingWindow.ExternalProgress)
            return TickHandedOff(s);
        if (s.Done && s.SinceDone > options.LingerSeconds) Begin(Stage.Moving);
        else if (s.Elapsed > options.MaxSeconds) Begin(s.Done ? Stage.Moving : Stage.Fading);
        return true;
    }

    // A mod shows its own progress (agm-114, PR #16): the full window hides at once and comes back as the summary card in its
    // place, without the glide, when Keel is done.
    private bool TickHandedOff(Progress.Snapshot s)
    {
        if (!handedOffFull)
        {
            handedOffFull = true;
            Native.ShowWindow(hwnd, Native.SW_HIDE);
        }
        if (!s.Done)
            return false;
        Begin(Stage.Summary);
        summaryStartTicks = DateTime.UtcNow.Ticks;
        summary = s;
        now = SummaryRect();
        Native.SetWindowPos(hwnd, IntPtr.Zero, now.Left, now.Top, now.Right - now.Left, now.Bottom - now.Top, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
        PublishSummary();
        Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
        return true;
    }

    private bool TickMoving(double t)
    {
        double k = Math.Min(1.0, t / MoveSeconds), e = 1 - Math.Pow(1 - k, 3);
        now = Lerp(from, to, e);
        Native.SetWindowPos(hwnd, IntPtr.Zero, now.Left, now.Top, now.Right - now.Left, now.Bottom - now.Top, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
        if (k < 1.0)
            return true;
        stage = Stage.Summary; stageStart = Now;
        PublishSummary();
        return true;
    }

    // Where the card is, in pixels of the game window: the game-drawn card takes over right there.
    private void PublishSummary()
    {
        LoadingWindow.SummaryX = now.Left - game.Left; LoadingWindow.SummaryY = now.Top - game.Top;
        LoadingWindow.SummaryW = now.Right - now.Left; LoadingWindow.SummaryH = now.Bottom - now.Top;
        LoadingWindow.SummaryScale = scale;
        LoadingWindow.SummaryReady = true;
    }

    private bool TickSummary(double t)
    {
        if (LoadingWindow.GameDrawsSummary) Begin(Stage.HandOff);           // the game draws the card now
        else if (LoadingWindow.OnMenuSubPage)                               // Scenarios, Options ...: gone at once
        {
            Native.ShowWindow(hwnd, Native.SW_HIDE);
            Begin(Stage.Away);
            return false;
        }
        else if (options.SummarySeconds > 0 && t > options.SummarySeconds) Begin(Stage.Fading);
        return true;
    }

    private bool TickAway()
    {
        if (LoadingWindow.GameDrawsSummary) { Close(); return false; }
        if (LoadingWindow.OnMenuSubPage)
            return false;
        // back on the menu's top page: there again at once, with a fresh timer
        stage = Stage.Summary; stageStart = Now;
        SetAlpha(options.Opacity);
        Native.ShowWindow(hwnd, Native.SW_SHOWNOACTIVATE);
        return true;
    }

    private bool TickFade(double t, double seconds)
    {
        double k = Math.Min(1.0, t / seconds);
        SetAlpha(options.Opacity * (float)(1 - k));
        if (k < 1.0)
            return true;
        Close();
        return false;
    }

    private void Close()
    {
        Native.KillTimer(hwnd, new UIntPtr(1));
        Native.DestroyWindow(hwnd);
    }

    // ---- where it goes ---------------------------------------------------------------------------------------------------

    /// <summary>The game's window (class UnityWndClass of this process): its rectangle sizes and places everything; it becomes the owner.</summary>
    private void FindOwner()
    {
        uint pid = (uint)Process.GetCurrentProcess().Id;
        IntPtr found = IntPtr.Zero;
        var name = new StringBuilder(64);
        enumProc = (w, l) =>
        {
            Native.GetWindowThreadProcessId(w, out uint p);
            if (p != pid || !Native.IsWindowVisible(w)) return true;
            name.Clear();
            Native.GetClassNameW(w, name, 64);
            if (name.ToString() != "UnityWndClass") return true;
            found = w;
            return false;
        };
        Native.EnumWindows(enumProc, IntPtr.Zero);
        if (found == IntPtr.Zero) return;
        owner = found;
        Native.SetOwner(hwnd, owner);
        // Above the game right now - but only when the game is the program in front: with another program in front it stays behind it.
        if (Native.GetForegroundWindow() == owner)
            Native.SetWindowPos(hwnd, Native.HWND_TOP, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        // the game's window decides the size and the place from now on
        MeasureGame();
        ApplyScale();
        if (stage == Stage.Full && !options.Offscreen)
        {
            now = FullRect();
            Native.SetWindowPos(hwnd, IntPtr.Zero, now.Left, now.Top, now.Right - now.Left, now.Bottom - now.Top, Native.SWP_NOACTIVATE | Native.SWP_NOZORDER);
        }
    }

    private void MeasureGame()
    {
        if (owner != IntPtr.Zero && Native.GetWindowRect(owner, out game) && game.Right - game.Left > 200 && game.Bottom - game.Top > 200)
            return;
        game = new Native.RECT { Left = 0, Top = 0, Right = Native.GetSystemMetrics(Native.SM_CXSCREEN), Bottom = Native.GetSystemMetrics(Native.SM_CYSCREEN) };
    }

    private void ApplyScale()
    {
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        int dpi = Math.Max(96, Native.GetDeviceCaps(screen, Native.LOGPIXELSX));
        Native.ReleaseDC(IntPtr.Zero, screen);
        float s = options.Scale > 0 ? options.Scale : Layout.Scale(game.Bottom - game.Top, dpi / 96f);
        if (Math.Abs(s - scale) < 0.01f && fontText != IntPtr.Zero) return;
        scale = s;
        foreach (IntPtr f in new[] { fontText, fontSmall })
            if (f != IntPtr.Zero) Native.DeleteObject(f);
        fontText = Font(Layout.TextPoints, 400, scale);
        fontSmall = Font(Layout.SmallPoints, 400, scale);
    }

    private Native.RECT FullRect()
    {
        int gw = game.Right - game.Left, gh = game.Bottom - game.Top;
        int w = (int)(Layout.Width * scale), h = (int)(Layout.Height(Layout.Rows) * scale), margin = (int)(24 * scale);
        int x, y;
        switch (options.Spot)
        {
            case Spot.TopLeft: x = game.Left + margin; y = game.Top + margin; break;
            case Spot.TopRight: x = game.Right - w - margin; y = game.Top + margin; break;
            case Spot.BottomLeft: x = game.Left + margin; y = game.Bottom - h - margin; break;
            case Spot.BottomRight: x = game.Right - w - margin; y = game.Bottom - h - margin; break;
            default:                                            // the marker: centred in the blank band under the main menu's news box
                x = game.Left + (int)(gw * 0.588) - w / 2;
                y = game.Top + (int)(gh * 0.770) - h / 2;
                break;
        }
        x = Math.Max(game.Left, Math.Min(game.Right - w, x));
        y = Math.Max(game.Top, Math.Min(game.Bottom - h, y));
        return new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
    }

    /// <summary>Bottom left, just above the game's version text.</summary>
    private Native.RECT SummaryRect()
    {
        int gh = game.Bottom - game.Top;
        int w = (int)(Layout.SummaryWidth * scale), h = (int)(Layout.SummaryHeight * scale);
        int x = game.Left + (int)(Layout.SummaryMargin * scale), y = game.Bottom - (int)(gh * Layout.SummaryBottomShare) - h;
        return new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
    }

    private static Native.RECT Lerp(Native.RECT a, Native.RECT b, double e) => new Native.RECT
    {
        Left = (int)(a.Left + (b.Left - a.Left) * e), Top = (int)(a.Top + (b.Top - a.Top) * e),
        Right = (int)(a.Right + (b.Right - a.Right) * e), Bottom = (int)(a.Bottom + (b.Bottom - a.Bottom) * e),
    };

    // ---- painting ---------------------------------------------------------------------------------------------------------

    private void Paint()
    {
        if (hwnd == IntPtr.Zero) return;
        Native.GetClientRect(hwnd, out Native.RECT rc);
        int w = rc.Right, h = rc.Bottom;
        if (w <= 0 || h <= 0) return;
        IntPtr dc = Native.GetDC(hwnd);
        IntPtr mem = Native.CreateCompatibleDC(dc);
        IntPtr bmp = Native.CreateCompatibleBitmap(dc, w, h);
        IntPtr old = Native.SelectObject(mem, bmp);
        bool squeezed = stage == Stage.Moving && (Now - stageStart) / MoveSeconds < 0.55 && snapDc != IntPtr.Zero;
        if (stage == Stage.Full)
            Draw(mem, w, h, progress.Take(Layout.Rows), scale, fontText, fontSmall, LoadingWindow.WaitingFor);
        else if (squeezed)          // the full window, squeezed along while it shrinks
        {
            Native.SetStretchBltMode(mem, Native.HALFTONE);
            Native.StretchBlt(mem, 0, 0, w, h, snapDc, 0, 0, snapW, snapH, Native.SRCCOPY);
        }
        else
            DrawSummary(mem, w, h, summary ?? progress.Take(0), scale, fontSmall, LoadingWindow.Alert);
        Native.BitBlt(dc, 0, 0, w, h, mem, 0, 0, Native.SRCCOPY);
        Native.SelectObject(mem, old);
        Native.DeleteObject(bmp);
        Native.DeleteDC(mem);
        Native.ReleaseDC(hwnd, dc);
    }

    /// <summary>The full window as it is now, kept for the move.</summary>
    private void TakeSnapshot()
    {
        FreeSnapshot();
        Native.GetClientRect(hwnd, out Native.RECT rc);
        snapW = rc.Right; snapH = rc.Bottom;
        IntPtr dc = Native.GetDC(hwnd);
        snapDc = Native.CreateCompatibleDC(dc);
        snapBmp = Native.CreateCompatibleBitmap(dc, snapW, snapH);
        snapOld = Native.SelectObject(snapDc, snapBmp);
        Native.ReleaseDC(hwnd, dc);
        Draw(snapDc, snapW, snapH, progress.Take(Layout.Rows), scale, fontText, fontSmall, LoadingWindow.WaitingFor);
    }

    private void FreeSnapshot()
    {
        if (snapDc == IntPtr.Zero) return;
        Native.SelectObject(snapDc, snapOld);
        Native.DeleteObject(snapBmp);
        Native.DeleteDC(snapDc);
        snapDc = snapBmp = snapOld = IntPtr.Zero;
    }

    internal static IntPtr Font(int points, int weight, float scale) =>
        Native.CreateFontW(-(int)Math.Round(points * 96f / 72f * scale), 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5 /* CLEARTYPE */, 0, "Segoe UI");

    // ---- drawing (shared with the preview tool) ---------------------------------------------------------------------------

    /// <summary>
    /// The sizes of both windows, in pixels at scale 1. The summary card is drawn here and again by the game once it takes over
    /// (KeelHost.DrawSummaryCard): both go by these numbers.
    /// </summary>
    internal static class Layout
    {
        public const int Width = 460, Rows = 8, TextPoints = 12, SmallPoints = 10;
        public static int Height(int rows) => 14 + 24 + 22 + 22 + rows * 19 + 8 + 20 + 14;

        public const int SummaryWidth = 300, SummaryHeight = 80, SummaryMargin = 8;
        public const double SummaryBottomShare = 0.05;          // the game's version text below the card
        public const int Pad = 10, HeaderHeight = 22, FirstLine = 24, LineHeight = 17, CreditWidth = 130;
        public const string Credit = "Made by Kanalratte";
        public const string ProfileUrl = "https://steamcommunity.com/profiles/76561198169349193";

        /// <summary>By the game's height (1.5 on a 4K screen: the window then fits the marker's band), never below the DPI scale.</summary>
        public static float Scale(int gameHeight, float dpiScale) => Math.Max(Math.Max(1f, dpiScale), gameHeight / 1080f * 0.75f);

        /// <summary>The credit line on the summary card, in its pixels: a click there opens the author's profile.</summary>
        public static Box CreditSpot(float scale)
        {
            int S(int v) => (int)Math.Round(v * scale);
            return new Box(S(Pad), S(FirstLine + 2 * LineHeight), S(CreditWidth), S(LineHeight));
        }
    }

    internal readonly struct Box
    {
        public readonly int X, Y, W, H;
        public Box(int x, int y, int w, int h) { X = x; Y = y; W = w; H = h; }
        public bool Contains(int x, int y) => x >= X && x <= X + W && y >= Y && y <= Y + H;
    }

    private static readonly uint Back = Native.Rgb(KeelPalette.Panel), Header = Native.Rgb(KeelPalette.Header),
        Track = Native.Rgb(KeelPalette.Control), Accent = Native.Rgb(KeelPalette.Accent), Amber = Native.Rgb(KeelPalette.Amber),
        Text = Native.Rgb(KeelPalette.Text), Dim = Native.Rgb(KeelPalette.Muted),
        Green = Native.Rgb(KeelPalette.Green), Red = Native.Rgb(KeelPalette.Red);

    /// <summary>
    /// Draws the full window's content into <paramref name="dc"/> (size w x h pixels, at <paramref name="scale"/> x 96 dpi).
    /// <paramref name="waitingFor"/>: what the game waits for the player to do, or null.
    /// </summary>
    internal static void Draw(IntPtr dc, int w, int h, Progress.Snapshot s, float scale, IntPtr fontText, IntPtr fontSmall, string waitingFor)
    {
        int S(int v) => (int)Math.Round(v * scale);
        Fill(dc, 0, 0, w, h, Back);
        Native.SetBkMode(dc, Native.TRANSPARENT);
        int pad = S(10), y = 0;

        Fill(dc, 0, 0, w, S(28), Header);
        Put(dc, fontSmall, Dim, "R.A.T.S. KEEL  ·  STARTUP", pad, y, w - 2 * pad - S(70), S(28), 0);
        Put(dc, fontSmall, Dim, Progress.Clock(s.Elapsed), pad, y, w - 2 * pad, S(28), Native.DT_RIGHT);
        y += S(36);

        Put(dc, fontText, Text, s.Phase, pad, y, w - 2 * pad, S(22), Native.DT_END_ELLIPSIS);
        y += S(22);

        DrawBar(dc, w, s, scale, fontSmall, y);
        y += S(22);

        int rowH = S(19);
        // A preparation DLL's line sits in the top row while the list has not filled it yet.
        string preparing = LoadingWindow.PreparationStatus;
        if (!string.IsNullOrEmpty(preparing) && s.Recent.Count < Layout.Rows)
            Put(dc, fontText, Dim, preparing, pad, y, w - 2 * pad, rowH, Native.DT_END_ELLIPSIS);
        for (int i = 0; i < Layout.Rows; i++)
        {
            int idx = i - (Layout.Rows - s.Recent.Count);
            if (idx >= 0)
                DrawRow(dc, w, s.Recent[idx], scale, fontText, fontSmall, y);
            y += rowH;
        }
        y += S(8);

        (string foot, uint footColor) = Foot(s, waitingFor);
        Put(dc, fontSmall, footColor, foot, pad, y, w - 2 * pad, S(20), Native.DT_END_ELLIPSIS);
    }

    // A fill when the number of mods is known, else a block that travels.
    private static void DrawBar(IntPtr dc, int w, Progress.Snapshot s, float scale, IntPtr fontSmall, int y)
    {
        int S(int v) => (int)Math.Round(v * scale);
        int pad = S(10), barW = w - 2 * pad - S(60), barH = S(4), barY = y + (S(18) - barH) / 2;
        Fill(dc, pad, barY, barW, barH, Track);
        if (s.Total > 0)
        {
            int fill = (int)(barW * Math.Min(1.0, s.Handled / (double)s.Total));
            Fill(dc, pad, barY, fill, barH, Accent);
            Put(dc, fontSmall, Dim, s.Handled + " / " + s.Total, pad + barW + S(8), y, S(52), S(18), Native.DT_RIGHT);
            return;
        }
        int block = barW / 5, x = (int)(((s.Elapsed * 0.6) % 1.0) * (barW - block));
        Fill(dc, pad + x, barY, block, barH, Accent);
    }

    private static void DrawRow(IntPtr dc, int w, ModLine m, float scale, IntPtr fontText, IntPtr fontSmall, int y)
    {
        int S(int v) => (int)Math.Round(v * scale);
        int pad = S(10), rowH = S(19);
        (uint c, string mark) = Mark(m.State);
        Put(dc, fontSmall, c, mark, pad, y, S(22), rowH, 0);
        Put(dc, fontText, c == Dim ? Dim : Text, m.Name, pad + S(26), y, w - 2 * pad - S(26) - S(64), rowH, Native.DT_END_ELLIPSIS);
        if (m.TookSeconds >= 0.5)
            Put(dc, fontSmall, m.TookSeconds >= 5 ? Amber : Dim, m.TookSeconds.ToString("0.0") + " s", pad, y, w - 2 * pad, rowH, Native.DT_RIGHT);
    }

    private static (uint color, string mark) Mark(PluginState state) => state switch
    {
        PluginState.Running => (Green, "ok"),
        PluginState.RunningWithErrors => (Amber, "!"),
        PluginState.Skipped or PluginState.LeftToAnchorChain => (Dim, "-"),
        _ => (Red, "x"),
    };

    private static (string text, uint color) Foot(Progress.Snapshot s, string waitingFor)
    {
        if (s.Done)
            return ("All mods started.", Green);
        if (s.Current != null)    // Keel says which mod it starts before each start, so the slow mod is named while it is slow
            return ("Starting " + s.CurrentIndex + "/" + s.Total + ": " + Shorten(s.Current, 42) + (s.CurrentFor >= 1 ? "  -  " + s.CurrentFor.ToString("0") + " s" : ""),
                    s.CurrentFor >= 5 ? Amber : Text);
        if (waitingFor != null)
            return ("Waiting for you: " + waitingFor, Amber);
        if (s.Idle >= 2.5)
            return ("Still loading  -  " + s.Idle.ToString("0") + " s since the last message" + (string.IsNullOrEmpty(s.LastEvent) ? "" : ": " + Shorten(s.LastEvent, 34)), Amber);
        return ("Click to hide.", Dim);
    }

    /// <summary>The summary card: what Keel did, how long the game took, what to look at. <paramref name="alert"/>: a crash note, or null.</summary>
    internal static void DrawSummary(IntPtr dc, int w, int h, Progress.Snapshot s, float scale, IntPtr fontSmall, string alert)
    {
        int S(int v) => (int)Math.Round(v * scale);
        Fill(dc, 0, 0, w, h, Back);
        bool amber = alert != null || s.HasProblems;
        Fill(dc, 0, 0, w, S(Layout.HeaderHeight), Header);
        Native.SetBkMode(dc, Native.TRANSPARENT);
        int pad = S(Layout.Pad), y = 0, lineH = S(Layout.LineHeight);

        Put(dc, fontSmall, Dim, s.Title, pad, y, w - 2 * pad, S(Layout.HeaderHeight), Native.DT_END_ELLIPSIS);
        y += S(Layout.FirstLine);
        Put(dc, fontSmall, Text, s.ReadyLine, pad, y, w - 2 * pad, lineH, Native.DT_END_ELLIPSIS);
        y += lineH;
        Put(dc, fontSmall, amber ? Amber : Green, s.SummaryLine(alert), pad, y, w - 2 * pad, lineH, Native.DT_END_ELLIPSIS);
        y += lineH;
        Put(dc, fontSmall, Dim, Layout.Credit, pad, y, S(Layout.CreditWidth), lineH, Native.DT_END_ELLIPSIS);
    }

    private static string Shorten(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "...";

    private static void Fill(IntPtr dc, int x, int y, int w, int h, uint rgb)
    {
        if (w <= 0 || h <= 0) return;
        var r = new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        IntPtr brush = Native.CreateSolidBrush(rgb);
        Native.FillRect(dc, ref r, brush);
        Native.DeleteObject(brush);
    }

    private static void Put(IntPtr dc, IntPtr font, uint color, string text, int x, int y, int w, int h, uint flags)
    {
        if (string.IsNullOrEmpty(text)) return;
        IntPtr old = Native.SelectObject(dc, font);
        Native.SetTextColor(dc, color);
        var r = new Native.RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
        Native.DrawTextW(dc, text, text.Length, ref r, Native.DT_SINGLELINE | Native.DT_VCENTER | Native.DT_NOPREFIX | flags);
        Native.SelectObject(dc, old);
    }
}
