using System;
using System.IO;
using System.Linq;

namespace KeelFixture;

public static class Preparation
{
    public static void Prepare(string streamingAssets, string[] enabledMods, Action<string> report)
    {
        string name = typeof(Preparation).Assembly.GetName().Name;
        if (name.Contains(".Failure."))
        {
            AppDomain.CurrentDomain.SetData("KeelFixture.Failure", true);
            throw new InvalidOperationException("KEEL_FIXTURE intentional preparation failure");
        }
        if (name.Contains(".Disabled."))
            throw new InvalidOperationException("KEEL_FIXTURE disabled callback ran");

        string folder = enabledMods.Single(path => Path.GetFileName(path) == "KeelEarlyFixture");
        if (!Path.GetFullPath(Path.GetDirectoryName(folder)).Equals(Path.GetFullPath(streamingAssets), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Wrong streaming assets path.");
        string input = File.ReadAllText(Path.Combine(folder, "fixture-input.txt"));
        AppDomain.CurrentDomain.SetData("KeelFixture.PreparationFolders", enabledMods);
        AppDomain.CurrentDomain.SetData("KeelFixture.PreparationThread", Environment.CurrentManagedThreadId);
        report("KEEL_FIXTURE prepared " + input);
        AppDomain.CurrentDomain.SetData("KeelFixture.Prepared", input);
    }
}
