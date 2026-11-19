using System;
using System.Drawing;
using System.Windows.Forms;

namespace SeaPowerKeel;

/// <summary>
/// The look of Keel's own windows outside the game (KeelSetup, the crash window, the send window): white, Segoe UI, one column of
/// lines and controls, the buttons in a row from the right edge. Sizes are in pixels at 96 dpi and scaled for the screen.
/// Shared source: compiled into KeelSetup and KeelWatcher.
/// </summary>
internal sealed class DialogKit
{
    private readonly float dpi;
    /// <summary>The width of the column's text, scaled.</summary>
    public readonly int Wide;
    public static readonly Font Heading = new("Segoe UI Semibold", 15f);
    public static readonly Color HeadingColor = Color.FromArgb(32, 32, 32), TextColor = Color.FromArgb(48, 48, 48);

    public DialogKit(int width)
    {
        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            dpi = g.DpiX;
        Wide = P(width);
    }

    public int P(int v) => (int)Math.Round(v * dpi / 96f);

    /// <summary>A fixed dialog that grows with its column; the caller sets its title, place and owner.</summary>
    public void Style(Form form)
    {
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MinimizeBox = form.MaximizeBox = false;
        form.AutoSize = true;
        form.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        form.BackColor = Color.White;
        form.Font = new Font("Segoe UI", 10f);
        form.Padding = new Padding(0, 0, P(26), P(20));
    }

    // Placed by hand: an auto-sized form ignores its left and top padding for a control that is not docked.
    public FlowLayoutPanel Column() => new()
    {
        Location = new Point(P(26), P(22)), FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
    };

    /// <summary>A line of text as wide as the column, wrapping, with <paramref name="below"/> pixels under it.</summary>
    public Label Line(string text, Font font, Color color, int below) => new()
    {
        Text = text, Font = font, ForeColor = color, AutoSize = true, MinimumSize = new Size(Wide, 0), MaximumSize = new Size(Wide, 0),
        Margin = new Padding(0, 0, 0, P(below)),
    };

    /// <summary>A button of the row; <paramref name="left"/> and <paramref name="right"/> are its gaps to its neighbours.</summary>
    public Button Button(string text, int left = 0, int right = 0) => new()
    {
        Text = text, AutoSize = true, Padding = new Padding(P(10), P(2), P(10), P(2)), Margin = new Padding(P(left), 0, P(right), 0),
    };

    /// <summary>The button row: right to left, so the first one added sits at the right edge.</summary>
    public static FlowLayoutPanel Row() => new()
    {
        FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Anchor = AnchorStyles.Right, Margin = new Padding(0),
    };
}
