using AgentSpeed.Core;
using AgentSpeed.Presentation.Modes;

namespace AgentSpeed.Presentation.History;

/// <summary>The color of a row's share bar in the effort and speed section (DESIGN 4b, 3b).</summary>
public enum ModeHistoryTone
{
    Neutral,

    /// <summary>Effort <c>max</c>: heat 0.82.</summary>
    Max,

    /// <summary>Effort <c>ultra</c> / <c>ultracode</c>: the top rung's gradient.</summary>
    TopRung,

    /// <summary>A speed with bolts.</summary>
    Amber,

    /// <summary>The unknown bucket: label 12 %.</summary>
    Unknown,
}

/// <summary>One of a row's top models with its share of the row's output.</summary>
public sealed record ModeHistoryModel(string Model, double Share);

/// <summary>
/// One value of the selected kind over the range, summing models, providers and evidence. <see cref="Mode"/> is
/// <c>unknown</c> for the unknown bucket.
/// </summary>
/// <param name="Share">Share of the range's output.</param>
public sealed record ModeHistoryRow(
    string Mode,
    long Requests,
    long Output,
    long MeasuredOutput,
    long MeasuredMs,
    double Share,
    IReadOnlyList<ModeHistoryModel> TopModels)
{
    public bool IsUnknown => Mode == "unknown";

    public double? AverageSpeed => MeasuredMs > 0 ? MeasuredOutput / (MeasuredMs / 1000.0) : null;
}

/// <summary>The section's rows, in display order with the unknown bucket last; they add up to the range's totals.</summary>
/// <param name="HasKnown">Some value of the kind is known in the range (else only the unknown row and the empty note show).</param>
public sealed record ModeHistorySection(ExecutionModeKind Kind, IReadOnlyList<ModeHistoryRow> Rows, bool HasKnown)
{
    /// <summary>The leading glyph: an effort row's meter, a speed row's bolts (none: a hollow ring); null for unknown.</summary>
    public EffortCapsule? Glyph(ModeHistoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        return row.IsUnknown ? null : Kind == ExecutionModeKind.Effort ? new EffortCapsule(row.Mode, null) : new EffortCapsule(null, row.Mode);
    }

    public ModeHistoryTone Tone(ModeHistoryRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.IsUnknown) return ModeHistoryTone.Unknown;
        if (Kind == ExecutionModeKind.Speed) return EffortAndSpeed.Bolts(row.Mode) > 0 ? ModeHistoryTone.Amber : ModeHistoryTone.Neutral;
        return EffortAndSpeed.IsTopRung(row.Mode) ? ModeHistoryTone.TopRung : row.Mode == "max" ? ModeHistoryTone.Max : ModeHistoryTone.Neutral;
    }
}

/// <summary>The history's effort and speed section (DESIGN 4b, 3b; EXECUTION-MODES, Local aggregates and history).</summary>
public static class ModeHistory
{
    public const int TopModelCount = 3;

    /// <param name="days">The range's days for the selected source.</param>
    /// <param name="kind"><see cref="ExecutionModeKind.Effort"/> or <see cref="ExecutionModeKind.Speed"/>.</param>
    public static ModeHistorySection Build(IEnumerable<DaySummary> days, ExecutionModeKind kind)
    {
        ArgumentNullException.ThrowIfNull(days);
        var known = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        var unknown = new Bucket();
        long requests = 0, output = 0, measuredOutput = 0, measuredMs = 0;
        foreach (var day in days)
        {
            requests += day.Requests;
            output += day.Output;
            measuredOutput += day.MeasuredOutput;
            measuredMs += day.MeasuredMs;
            var knownByModel = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var row in day.Modes)
            {
                if (row.Kind != kind || row.Mode == "unknown")
                {
                    continue;
                }

                var bucket = known.TryGetValue(row.Mode, out var existing) ? existing : known[row.Mode] = new Bucket();
                bucket.Add(row.Model, row.Requests, row.Output, row.MeasuredOutput, row.MeasuredMs);
                knownByModel[row.Model] = knownByModel.GetValueOrDefault(row.Model) + row.Output;
            }

            foreach (var model in day.Models)
            {
                unknown.AddModel(model.Model, Math.Max(0, model.Output - knownByModel.GetValueOrDefault(model.Model)));
            }
        }

        var rows = known.OrderBy(p => p.Key, EffortAndSpeed.Order(kind)).Select(p => p.Value.Row(p.Key, output)).ToList();
        var rest = new Bucket
        {
            Requests = Math.Max(0, requests - rows.Sum(r => r.Requests)),
            Output = Math.Max(0, output - rows.Sum(r => r.Output)),
            MeasuredOutput = Math.Max(0, measuredOutput - rows.Sum(r => r.MeasuredOutput)),
            MeasuredMs = Math.Max(0, measuredMs - rows.Sum(r => r.MeasuredMs)),
            Models = unknown.Models,
        };
        if (rows.Count == 0 || rest.Requests > 0 || rest.Output > 0)
        {
            rows.Add(rest.Row("unknown", output));
        }

        return new ModeHistorySection(kind, rows, HasKnown: known.Count > 0);
    }

    private sealed class Bucket
    {
        public long Requests { get; set; }

        public long Output { get; set; }

        public long MeasuredOutput { get; set; }

        public long MeasuredMs { get; set; }

        public Dictionary<string, long> Models { get; init; } = new(StringComparer.Ordinal);

        public void Add(string model, long requests, long output, long measuredOutput, long measuredMs)
        {
            Requests += Math.Max(0, requests);
            Output += Math.Max(0, output);
            MeasuredOutput += Math.Max(0, measuredOutput);
            MeasuredMs += Math.Max(0, measuredMs);
            AddModel(model, output);
        }

        public void AddModel(string model, long output)
        {
            if (!string.IsNullOrWhiteSpace(model) && output > 0)
            {
                Models[model] = Models.GetValueOrDefault(model) + output;
            }
        }

        public ModeHistoryRow Row(string mode, long rangeOutput)
        {
            var modelOutput = Models.Values.Sum();
            var top = Models.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).Take(TopModelCount)
                .Select(p => new ModeHistoryModel(p.Key, modelOutput > 0 ? (double)p.Value / Math.Max(Output, modelOutput) : 0))
                .ToArray();
            return new ModeHistoryRow(mode, Requests, Output, MeasuredOutput, MeasuredMs, rangeOutput > 0 ? (double)Output / rangeOutput : 0, top);
        }
    }
}
