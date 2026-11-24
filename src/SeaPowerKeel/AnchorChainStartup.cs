using System;
using System.Collections.Generic;

namespace SeaPowerKeel;

internal static class AnchorChainStartup
{
    internal enum Wait { StartupDone, RetryFolders }
    internal const float RetrySeconds = 0.5f;

    // The caller drives this iterator from Awake. The discovery deadline starts after readiness.
    internal static IEnumerable<Wait> Discover(Func<bool> tryLoad, Func<float> realtime)
    {
        if (tryLoad()) yield break;
        yield return Wait.StartupDone;
        float deadline = realtime() + 60f;
        while (realtime() < deadline && !tryLoad())
            yield return Wait.RetryFolders;
    }
}
