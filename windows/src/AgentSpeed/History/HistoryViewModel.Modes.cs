using System.Collections.ObjectModel;
using AgentSpeed.Core;
using AgentSpeed.Modes;
using AgentSpeed.Presentation.History;

namespace AgentSpeed.History;

/// <summary>The effort and speed section (DESIGN 4b, 3b): one row per value of the chosen kind over the range and source.</summary>
internal sealed partial class HistoryViewModel
{
    private ExecutionModeKind _modeKind = ExecutionModeKind.Effort;
    private Action<ExecutionModeKind>? _modeKindChanged;
    private CapsuleAppearance _modeAppearance;
    private bool _modesEmpty;

    /// <summary>The rows in display order with the unknown bucket last; rows are kept and moved, so they spring to place.</summary>
    public ObservableCollection<ModeRowView> ModeRows { get; } = [];

    /// <summary><see cref="ExecutionModeKind.Effort"/> or <see cref="ExecutionModeKind.Speed"/>.</summary>
    public ExecutionModeKind ModeKind => _modeKind;

    /// <summary>No value of the kind is known in the range: only the unknown row shows, with <c>modes.history.empty</c>.</summary>
    public bool ModesEmpty { get => _modesEmpty; private set => Set(ref _modesEmpty, value); }

    /// <summary>How the rows' glyphs and bars draw.</summary>
    public CapsuleAppearance ModeAppearance { get => _modeAppearance; private set => Set(ref _modeAppearance, value); }

    /// <param name="kind">The remembered kind.</param>
    /// <param name="changed">Persists a new choice.</param>
    public void RememberModeKind(ExecutionModeKind kind, Action<ExecutionModeKind> changed)
    {
        _modeKind = kind == ExecutionModeKind.Speed ? ExecutionModeKind.Speed : ExecutionModeKind.Effort;
        _modeKindChanged = changed;
        UpdateModes(rebuild: true);
    }

    public void SetModeKind(ExecutionModeKind kind)
    {
        if (kind == _modeKind || kind is not (ExecutionModeKind.Effort or ExecutionModeKind.Speed))
        {
            return;
        }

        _modeKind = kind;
        _modeKindChanged?.Invoke(kind);
        UpdateModes(rebuild: true);
        OnPropertyChanged(nameof(ModeKind));
    }

    public void SetModeAppearance(CapsuleAppearance appearance)
    {
        if (appearance == _modeAppearance)
        {
            return;
        }

        ModeAppearance = appearance;
        UpdateModes(rebuild: false);
    }

    /// <summary>Matches rows by value and moves them into the new order; a new kind replaces them.</summary>
    private void UpdateModes(bool rebuild)
    {
        var section = ModeHistory.Build(Days, _modeKind);
        ModesEmpty = !section.HasKnown;
        if (rebuild)
        {
            ModeRows.Clear();
        }

        var existing = ModeRows.ToDictionary(r => r.Mode, StringComparer.Ordinal);
        for (var i = 0; i < section.Rows.Count; i++)
        {
            var row = section.Rows[i];
            if (!existing.Remove(row.Mode, out var view))
            {
                view = new ModeRowView(row.Mode);
                ModeRows.Insert(Math.Min(i, ModeRows.Count), view);
            }
            else if (ModeRows.IndexOf(view) is var at && at != i)
            {
                ModeRows.Move(at, i);
            }

            view.Apply(row, section, _loc, _modeAppearance);
        }

        foreach (var stale in existing.Values)
        {
            ModeRows.Remove(stale);
        }
    }
}
