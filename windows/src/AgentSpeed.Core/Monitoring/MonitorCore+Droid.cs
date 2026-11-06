using AgentSpeed.Core.Droid;
using AgentSpeed.Core.IO;

namespace AgentSpeed.Core.Monitoring;

/// <summary>
/// Factory Droid sessions (METRICS 4.16) are tracked files whose sidecar takes part in change detection: a sidecar path
/// reported by the watcher reads its transcript's session, and a session older than the startup window still reads its
/// sidecar once, off the monitor thread, to take the counters' high-water mark.
/// </summary>
internal sealed partial class MonitorCore
{
    /// <summary>The transcript of a sidecar inside a Factory Droid root; any other path as given.</summary>
    private string DroidTranscriptOf(string path)
    {
        if (!path.EndsWith(DroidPaths.SidecarSuffix, StringComparison.Ordinal))
        {
            return path;
        }

        foreach (var root in _roots)
        {
            if (root.Family == RootFamily.Droid && root.Enabled && DroidDiscovery.TranscriptOfSidecarPath(root.Path, path) is { } transcript)
            {
                return transcript;
            }
        }

        return path;
    }

    /// <summary>
    /// A session whose transcript starts at its end: an empty read from there whose sidecar read only sets <c>H</c>, so
    /// growth from then on counts (METRICS 4.16 Startup and history).
    /// </summary>
    private static ReadPlan PlanDroidAtEnd(TrackedFile tracked, DroidParser droid, DiscoveredFile file)
    {
        droid.StartAtEnd();
        return new ReadPlan(tracked, file.LastWriteUtc, file.Length, StartAlignment.SkipPartialLine, file.Length, Counted: false);
    }
}
