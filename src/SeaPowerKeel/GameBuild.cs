using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SeaPowerKeel;

internal class GameBuild(string version, string managed)
{
    private static readonly Regex VersionFormat = new(@"\A(?<release>[0-9]+\.[0-9]+\.[0-9]+)(?<tag>-[0-9A-Za-z][0-9A-Za-z.-]*)?\z");
    private readonly Lazy<string> sha256 = new(() => FileHash.Sha256(Path.Combine(managed, MethodSnapshot.AssemblyFile)));

    public string VersionText { get; } = version?.Trim();
    public Version Release { get; } = ParseRelease(version?.Trim(), out _);
    public string Sha256 => sha256.Value;

    internal static Version ParseRelease(string text, out bool tagged)
    {
        Match match = VersionFormat.Match(text ?? "");
        tagged = match.Groups["tag"].Success;
        return Version.TryParse(match.Groups["release"].Value, out Version release) ? release : null;
    }

}

internal class GameBuildRule(string text, GameBuildRule.VersionTest[] versions, string sha256 = null)
{
    public string Text { get; } = text;
    public bool UsesHash => sha256 != null;

    public bool Accepts(GameBuild build) => UsesHash
        ? string.Equals(sha256, build.Sha256, StringComparison.OrdinalIgnoreCase)
        : build.Release != null && versions.All(test => test.Accepts(build));

    public static GameBuildRule Parse(string text)
    {
        const string hashPrefix = "sha256:";
        if (text.StartsWith(hashPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string hash = text.Substring(hashPrefix.Length);
            return hash.Length == 64 && hash.All(Uri.IsHexDigit) ? new(text, null, hash) : null;
        }

        string[] terms = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        VersionTest[] versions = terms.Select(VersionTest.Parse).ToArray();
        return versions.Length > 0 && versions.All(test => test != null) ? new(text, versions) : null;
    }

    internal class VersionTest(string comparison, Version release, string exact)
    {
        public static VersionTest Parse(string term)
        {
            int length = term.TakeWhile(c => c is '<' or '>' or '=').Count();
            string comparison = term.Substring(0, length);
            string value = term.Substring(length);
            Version release = GameBuild.ParseRelease(value, out bool tagged);
            if (release == null || comparison is not ("" or ">" or ">=" or "<" or "<=") || (tagged && comparison != ""))
                return null;
            return new(comparison, release, tagged ? value : null);
        }

        public bool Accepts(GameBuild build) => comparison switch
        {
            ">" => build.Release > release,
            ">=" => build.Release >= release,
            "<" => build.Release < release,
            "<=" => build.Release <= release,
            _ => build.Release == release && (exact == null || string.Equals(exact, build.VersionText, StringComparison.OrdinalIgnoreCase)),
        };
    }
}
