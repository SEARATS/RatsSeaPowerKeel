namespace SeaPowerKeel;

// Shared by the in-game panels and the startup window, which draws before Unity is ready.
internal static class KeelPalette
{
    internal const uint Panel = 0x2B2D31, Header = 0x242629, Row = Header, Divider = 0x494E52;
    internal const uint Control = 0x393D40, Hover = 0x53595D, Pressed = 0x5F666A;
    internal const uint Accent = 0xC04A3C;
    internal const uint Text = 0xD8DADB, Muted = 0xADB2B6, Amber = 0xD5A876;
    internal const uint Green = 0x9ABD9B, Red = 0xE6A49C, Blue = 0xA6C0CF;
}
