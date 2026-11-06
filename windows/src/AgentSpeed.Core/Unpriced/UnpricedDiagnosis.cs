namespace AgentSpeed.Core.Unpriced;

/// <summary>Why a response has no price (METRICS 9.10 <i>Reason</i>).</summary>
public enum UnpricedReason
{
    /// <summary>Step 2 of METRICS 9.2: the model is empty.</summary>
    NoModel,

    /// <summary>No candidate key hits the table.</summary>
    NotInTable,

    /// <summary>A priced entry without fast prices was hit in fast mode.</summary>
    NoFastPrice,

    /// <summary>A candidate key matches the table's <c>unknown[]</c>: no public price on purpose.</summary>
    Unpriceable,

    /// <summary>Live only: the response's <c>endMs</c> has no date (METRICS 8).</summary>
    NoDate,
}

/// <param name="Canonical">The key the resolver stopped at; null for <c>noModel</c>, <c>noDate</c> and an empty exact key.</param>
/// <param name="Rule">The <c>reason</c> text of the table's <c>unknown</c> entry, only for <c>unpriceable</c>.</param>
public sealed record UnpricedDiagnosis(UnpricedReason Reason, string? Canonical = null, string? Rule = null)
{
    public static UnpricedDiagnosis NoDate { get; } = new(UnpricedReason.NoDate);
}

public static class UnpricedReasons
{
    /// <summary>The reason's name in the copied text and in <c>cost.reason.&lt;name&gt;</c>.</summary>
    public static string Name(UnpricedReason reason) => reason switch
    {
        UnpricedReason.NoModel => "noModel",
        UnpricedReason.NotInTable => "notInTable",
        UnpricedReason.NoFastPrice => "noFastPrice",
        UnpricedReason.Unpriceable => "unpriceable",
        UnpricedReason.NoDate => "noDate",
        _ => throw new ArgumentOutOfRangeException(nameof(reason)),
    };
}
