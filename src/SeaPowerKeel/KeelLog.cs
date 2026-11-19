using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Logging;

namespace SeaPowerKeel;

/// <summary>
/// <c>BepInEx\Keel-log.txt</c>: everything BepInEx logs (the game's own Unity log lines included, and every mod's), each line
/// with the time of day and the time since the game started, plus the mod an error belongs to when Keel can tell. BepInEx 5's
/// LogOutput.log has no times and is overwritten at every start; Keel keeps the last sessions (Keel-log.1.txt, .2.txt ...).
/// </summary>
internal sealed class KeelLog : ILogListener
{
    private readonly object gate = new();
    private readonly DateTime gameStart;
    private StreamWriter writer;
    private DateTime lastFlush = DateTime.UtcNow;
    private string lastKey;
    private int repeats;

    /// <summary>Set once plugins run: the display name of the mod an error text belongs to, or null.</summary>
    public Func<string, string> OwnerOf;

    public string Path { get; }

    public KeelLog(int keepSessions)
    {
        Path = System.IO.Path.Combine(Paths.BepInExRootPath, KeelNames.LogFile);
        try
        {
            gameStart = Process.GetCurrentProcess().StartTime;
        }
        catch (Exception)
        {
            gameStart = DateTime.Now;
        }
        Rotate(keepSessions);
        writer = new StreamWriter(new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(false));
        writer.WriteLine($"# {Keel.Name} {Keel.Version} log – game started {gameStart:yyyy-MM-dd HH:mm:ss}; times are local, +seconds since the game started");
        writer.Flush();
    }

    private void Rotate(int keep)
    {
        try
        {
            string Old(int n) => System.IO.Path.Combine(Paths.BepInExRootPath, $"Keel-log.{n}.txt");
            if (File.Exists(Old(keep)))
                File.Delete(Old(keep));
            for (int n = keep - 1; n >= 1; n--)
                if (File.Exists(Old(n)))
                    File.Move(Old(n), Old(n + 1));
            if (keep >= 1 && File.Exists(Path))
                File.Move(Path, Old(1));
        }
        catch (Exception)
        {
            // a locked old log (opened in an editor): this session overwrites the current one
        }
    }

    public void LogEvent(object sender, LogEventArgs e)
    {
        if (writer == null)
            return;
        DateTime now = DateTime.Now;
        string text = e.Data?.ToString() ?? "";
        string source = e.Source?.SourceName ?? "?";
        if (Repeated(source + "|" + e.Level + "|" + text))
            return;
        var line = new StringBuilder(text.Length + 64)
            .Append(now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)).Append(' ')
            .Append('+').Append((now - gameStart).TotalSeconds.ToString("0.000", CultureInfo.InvariantCulture)).Append("s ")
            .Append('[').Append(Level(e.Level)).Append(" : ").Append(source).Append("] ");
        string owner = (e.Level & (LogLevel.Error | LogLevel.Fatal)) != 0 && source != Keel.Name ? Owner(text) : null;
        if (owner != null)
            line.Append("[from mod: ").Append(owner).Append("] ");
        // Multi-line messages (stack traces) keep their lines, indented under the time stamp.
        line.Append(text.Replace("\r\n", "\n").TrimEnd('\n').Replace("\n", Environment.NewLine + "      "));
        Write(line.ToString(), (e.Level & (LogLevel.Warning | LogLevel.Error | LogLevel.Fatal)) != 0);
    }

    // A mod that logs the same line every frame would fill the disk: repeats are counted, not written.
    private bool Repeated(string key)
    {
        lock (gate)
        {
            if (key == lastKey)
            {
                repeats++;
                return true;
            }
            WriteRepeats();
            lastKey = key;
            return false;
        }
    }

    // The mod an error came from, when its stack says so.
    private string Owner(string text)
    {
        if (OwnerOf == null)
            return null;
        try
        {
            return OwnerOf(text);
        }
        catch (Exception)
        {
            return null;        // attribution is a nicety; never let it break logging
        }
    }

    // Warnings and worse at once (the game may die right after), the rest at most every half second.
    private void Write(string line, bool now)
    {
        lock (gate)
        {
            try
            {
                writer.WriteLine(line);
                if (now || (DateTime.UtcNow - lastFlush).TotalMilliseconds > 500)
                {
                    writer.Flush();
                    lastFlush = DateTime.UtcNow;
                }
            }
            catch (Exception)
            {
                // disk full or file gone: stop writing, never throw into the logger
                writer = null;
            }
        }
    }

    // Called under the lock.
    private void WriteRepeats()
    {
        if (repeats == 0 || writer == null)
            return;
        try
        {
            writer.WriteLine($"      … the line above repeated {repeats} more time(s), until {DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)}");
        }
        catch (Exception)
        {
            writer = null;
        }
        repeats = 0;
    }

    public void Flush()
    {
        lock (gate)
        {
            WriteRepeats();
            lastKey = null;
            try
            {
                writer?.Flush();
            }
            catch (Exception)
            {
                writer = null;
            }
        }
    }

    private static string Level(LogLevel l) =>
        (l & LogLevel.Fatal) != 0 ? "Fatal  " :
        (l & LogLevel.Error) != 0 ? "Error  " :
        (l & LogLevel.Warning) != 0 ? "Warning" :
        (l & LogLevel.Message) != 0 ? "Message" :
        (l & LogLevel.Info) != 0 ? "Info   " : "Debug  ";

    public void Dispose()
    {
        lock (gate)
        {
            try
            {
                writer?.Dispose();
            }
            catch (Exception)
            {
                // closing at quit
            }
            writer = null;
        }
    }
}
