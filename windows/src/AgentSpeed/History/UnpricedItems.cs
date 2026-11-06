using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using AgentSpeed.Presentation.Colors;
using AgentSpeed.Presentation.History;
using AgentSpeed.Theme;

namespace AgentSpeed.History;

/// <summary>A source group of the unknown-prices popover, with its dot and its distance from the group above.</summary>
internal sealed record UnpricedGroupItem(UnpricedGroupView View, bool IsFirst)
{
    public Brush Dot => HeatPalette.SourceColor(View.Source).ToBrush();

    /// <summary>12 DIP above every group but the first (DESIGN 9).</summary>
    public Thickness Margin => new(0, IsFirst ? 0 : 12, 0, 0);

    public bool HasMore => View.More.Length > 0;
}

/// <summary>Groups with the same texts and rows, so a live recomputation that changes nothing keeps the popover's items.</summary>
internal sealed class UnpricedGroupComparer : IEqualityComparer<UnpricedGroupItem>
{
    public static UnpricedGroupComparer Instance { get; } = new();

    public bool Equals(UnpricedGroupItem? x, UnpricedGroupItem? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null && x.IsFirst == y.IsFirst && x.View.Source == y.View.Source
            && x.View.Name == y.View.Name && x.View.Requests == y.View.Requests && x.View.AccessibleName == y.View.AccessibleName
            && x.View.More == y.View.More && x.View.Rows.SequenceEqual(y.View.Rows));

    public int GetHashCode(UnpricedGroupItem obj) => HashCode.Combine(obj.View.Source, obj.View.Rows.Count);
}

/// <summary>
/// The second line of an unknown-price row after its reason chip: provider, Fast with one bolt, the lookup key, each
/// after a <c>·</c> (DESIGN 9). Set <see cref="RowProperty"/> on a <see cref="TextBlock"/>.
/// </summary>
internal static class UnpricedDetailText
{
    public static readonly DependencyProperty RowProperty = DependencyProperty.RegisterAttached(
        "Row", typeof(UnpricedRowView), typeof(UnpricedDetailText), new PropertyMetadata(null, OnRowChanged));

    private const string Separator = " · ";
    private const string Lightning = "";

    public static UnpricedRowView? GetRow(DependencyObject element) => (UnpricedRowView?)element.GetValue(RowProperty);

    public static void SetRow(DependencyObject element, UnpricedRowView? value) => element.SetValue(RowProperty, value);

    private static void OnRowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock text)
        {
            return;
        }

        text.Inlines.Clear();
        if (e.NewValue is not UnpricedRowView row)
        {
            return;
        }

        foreach (var (detail, fast) in new[] { (row.Via, false), (row.FastLabel, true), (row.Lookup, false) })
        {
            if (detail.Length == 0)
            {
                continue;
            }

            text.Inlines.Add(new Run(Separator));
            if (fast)
            {
                var bolt = new Run(Lightning) { FontSize = Math.Max(8, text.FontSize - 1), BaselineAlignment = BaselineAlignment.Center };
                bolt.SetResourceReference(TextElement.FontFamilyProperty, "AppSymbolFontFamily");
                text.Inlines.Add(bolt);
                text.Inlines.Add(new Run("\u2009" + detail));
            }
            else
            {
                text.Inlines.Add(new Run(detail));
            }
        }
    }
}
