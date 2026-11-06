using AgentSpeed.Core;
using Vortice;
using Vortice.Direct2D1;

namespace AgentSpeed.Boards.Layers;

/// <summary>
/// PHASE 2 SEAM (spec/EXECUTION-MODES.md, DESIGN 2c Rows): an agent's effort and speed capsule inside a group row. The
/// capsule component comes from the execution-modes work and is wired with the styles' rows; until then the app supplies
/// none and rows leave its place empty. Boards depend only on <see cref="AgentStatus.Modes"/>.
/// </summary>
internal interface IBoardModeCapsule
{
    /// <summary>Width in points the capsule takes for <paramref name="modes"/> at <paramref name="height"/>; 0 shows nothing.</summary>
    double Measure(ExecutionModes modes, double height);

    /// <summary>Draws the capsule into <paramref name="box"/> (points of the row layer).</summary>
    void Paint(ID2D1DeviceContext context, ExecutionModes modes, RawRectF box);
}
