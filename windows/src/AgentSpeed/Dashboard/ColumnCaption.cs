using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using AgentSpeed.Presentation.Formatting;

namespace AgentSpeed.Dashboard;

/// <summary>
/// A column caption of the agents' header (DESIGN 4, Header): 11 DIP medium, secondary, one line, right-aligned to its
/// column, never truncated. Hidden from accessibility: every row says its values in words.
/// </summary>
internal sealed class ColumnCaption : TextBlock
{
    public ColumnCaption()
    {
        FontSize = AgentIntelligenceColumn.CaptionSize;
        FontWeight = FontWeights.Medium;
        TextAlignment = TextAlignment.Right;
        TextWrapping = TextWrapping.NoWrap;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Bottom;
        SetResourceReference(ForegroundProperty, "TextFillColorSecondaryBrush");
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new DecorativePeer(this);

    private sealed class DecorativePeer(ColumnCaption owner) : TextBlockAutomationPeer(owner)
    {
        protected override bool IsControlElementCore() => false;

        protected override bool IsContentElementCore() => false;
    }
}
