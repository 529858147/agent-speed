namespace AgentSpeed.Presentation.Settings.Orbs;

/// <summary>
/// What an orb's two-line readout shows (DESIGN 2b <c>traffic</c>, 10): the traffic of its own sources, the whole computer,
/// the live intelligence of its sources, or nothing. Add, Duplicate and Split copy it; Merge keeps the target's.
/// </summary>
public enum OrbTraffic
{
    Sources,
    Machine,
    Intelligence,
    Off,
}

public static class OrbTraffics
{
    public static string Key(OrbTraffic traffic) => traffic switch
    {
        OrbTraffic.Machine => "machine",
        OrbTraffic.Intelligence => "intelligence",
        OrbTraffic.Off => "off",
        _ => "sources",
    };

    /// <summary>Network traffic, which needs measuring (and, for sources, per-process data); intelligence needs neither.</summary>
    public static bool IsNetwork(OrbTraffic traffic) => traffic is OrbTraffic.Sources or OrbTraffic.Machine;

    /// <summary>Absent (orbs saved by earlier versions) reads as <see cref="OrbTraffic.Sources"/>; an unknown value as <see cref="OrbTraffic.Off"/>.</summary>
    public static OrbTraffic Parse(string? key) => key switch
    {
        null or "sources" => OrbTraffic.Sources,
        "machine" => OrbTraffic.Machine,
        "intelligence" => OrbTraffic.Intelligence,
        _ => OrbTraffic.Off,
    };
}
