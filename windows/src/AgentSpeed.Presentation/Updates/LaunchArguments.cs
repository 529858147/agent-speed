using System.Globalization;

namespace AgentSpeed.Presentation.Updates;

/// <summary>
/// Command-line flags: <c>--after-update &lt;old version&gt;</c> and <c>--wait-pid &lt;pid&gt;</c> (the relaunch after an
/// install, UPDATES section 5) and the development flags <c>--check-updates-now</c> and <c>--preview-history</c>.
/// Unknown arguments are ignored.
/// </summary>
public sealed record LaunchArguments(bool AfterUpdate, string? PreviousVersion, int? WaitPid, bool CheckUpdatesNow)
{
    public const string AfterUpdateFlag = "--after-update";
    public const string WaitPidFlag = "--wait-pid";
    public const string CheckUpdatesNowFlag = "--check-updates-now";
    public const string PreviewHistoryFlag = "--preview-history";

    /// <summary>Development flag: fill the Usage History window with generated days instead of the real history.</summary>
    public bool PreviewHistory { get; init; }

    public static LaunchArguments None { get; } = new(false, null, null, false);

    public static LaunchArguments Parse(IReadOnlyList<string>? args)
    {
        if (args is null || args.Count == 0)
        {
            return None;
        }

        var afterUpdate = false;
        string? previous = null;
        int? waitPid = null;
        var checkNow = false;
        var previewHistory = false;
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            var value = i + 1 < args.Count ? args[i + 1] : null;
            if (string.Equals(arg, AfterUpdateFlag, StringComparison.OrdinalIgnoreCase))
            {
                afterUpdate = true;
                if (SemanticVersion.TryParse(value, out var version))
                {
                    previous = version.ToString();
                    i++;
                }
            }
            else if (string.Equals(arg, WaitPidFlag, StringComparison.OrdinalIgnoreCase))
            {
                if (value is not null && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) && pid > 0)
                {
                    waitPid = pid;
                    i++;
                }
            }
            else if (string.Equals(arg, CheckUpdatesNowFlag, StringComparison.OrdinalIgnoreCase))
            {
                checkNow = true;
            }
            else if (string.Equals(arg, PreviewHistoryFlag, StringComparison.OrdinalIgnoreCase))
            {
                previewHistory = true;
            }
        }

        return new LaunchArguments(afterUpdate, previous, waitPid, checkNow) { PreviewHistory = previewHistory };
    }

    /// <summary>The arguments the installer passes to the new executable.</summary>
    public static IReadOnlyList<string> ForRelaunch(string previousVersion, int processId) =>
        [AfterUpdateFlag, previousVersion, WaitPidFlag, processId.ToString(CultureInfo.InvariantCulture)];
}
