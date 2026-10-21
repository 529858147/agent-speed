using System.Globalization;

namespace AgentSpeed.Presentation.Updates;

/// <summary>
/// Command-line flags: <c>--after-update &lt;old version&gt;</c> and <c>--wait-pid &lt;pid&gt;</c> (the relaunch after an
/// install, UPDATES section 5) and the development flags <c>--check-updates-now</c>, <c>--preview-history</c> and
/// <c>--demo-speed 24,96,400</c>.
/// Unknown arguments are ignored.
/// </summary>
public sealed record LaunchArguments(bool AfterUpdate, string? PreviousVersion, int? WaitPid, bool CheckUpdatesNow)
{
    public const string AfterUpdateFlag = "--after-update";
    public const string WaitPidFlag = "--wait-pid";
    public const string CheckUpdatesNowFlag = "--check-updates-now";
    public const string PreviewHistoryFlag = "--preview-history";
    public const string DemoSpeedFlag = "--demo-speed";
    private const int MaxDemoSpeeds = 32;

    /// <summary>Development flag: fill the Usage History window with generated days instead of the real history.</summary>
    public bool PreviewHistory { get; init; }

    /// <summary>
    /// Development flag: hold the live speed at these tok/s values in turn (normalized, comma separated), to inspect the
    /// speed titles and the zero-speed glyphs. Null when not given.
    /// </summary>
    public string? DemoSpeeds { get; init; }

    public IReadOnlyList<double> DemoSpeedValues => ParseSpeeds(DemoSpeeds);

    /// <summary>Comma-separated non-negative finite numbers; anything invalid yields an empty list.</summary>
    public static IReadOnlyList<double> ParseSpeeds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var values = new List<double>();
        foreach (var part in text.Split(',', StringSplitOptions.TrimEntries))
        {
            if (values.Count >= MaxDemoSpeeds
                || !double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                || !double.IsFinite(value) || value > 1e9)
            {
                return [];
            }

            values.Add(value);
        }

        return values;
    }

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
        string? demoSpeeds = null;
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
            else if (string.Equals(arg, DemoSpeedFlag, StringComparison.OrdinalIgnoreCase))
            {
                var speeds = ParseSpeeds(value);
                if (speeds.Count > 0)
                {
                    demoSpeeds = string.Join(',', speeds.Select(v => v.ToString("R", CultureInfo.InvariantCulture)));
                    i++;
                }
            }
        }

        return new LaunchArguments(afterUpdate, previous, waitPid, checkNow) { PreviewHistory = previewHistory, DemoSpeeds = demoSpeeds };
    }

    /// <summary>The arguments the installer passes to the new executable.</summary>
    public static IReadOnlyList<string> ForRelaunch(string previousVersion, int processId) =>
        [AfterUpdateFlag, previousVersion, WaitPidFlag, processId.ToString(CultureInfo.InvariantCulture)];
}
