using System;
using System.IO;
using System.Security.Cryptography;

namespace SeaPowerKeel;

/// <summary>SHA-256 of a file, the same text as Get-FileHash and the release's SHA256SUMS.txt (lowercase hex).</summary>
internal static class FileHash
{
    public static string Sha256(string path)
    {
        try
        {
            return ReadSha256(path);
        }
        catch (Exception)
        {
            // A missing or locked file only costs the checksum line, never the report.
            return "";
        }
    }

    internal static string ReadSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Hex(sha.ComputeHash(stream));
    }

    internal static string Hex(byte[] hash) => BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

    /// <summary>The first characters, enough to compare by eye in the report window.</summary>
    public static string Short(string hex) => hex.Length > 12 ? hex.Substring(0, 12) + "..." : hex;
}
