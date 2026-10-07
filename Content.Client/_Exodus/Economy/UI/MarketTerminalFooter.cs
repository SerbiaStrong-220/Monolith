// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Client.UserInterface.Controls;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Exodus.Economy.UI;

/// <summary>
/// Shared footer strip for sector trade terminals.
/// </summary>
public sealed class MarketTerminalFooter : BoxContainer
{
    public MarketTerminalFooter()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SetHeight = 22;

        var stripe = new StripeBack
        {
            HasBottomEdge = false,
            HasMargins = false,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var label = new Label
        {
            Text = Loc.GetString("economy-terminal-footer"),
            VerticalAlignment = VAlignment.Center,
            HorizontalAlignment = HAlignment.Right,
            HorizontalExpand = true,
            Margin = new Thickness(8, 0),
            StyleClasses = { "PdaContentFooterText" },
            FontColorOverride = MarketTerminalTheme.TextMuted,
        };

        stripe.AddChild(label);
        AddChild(stripe);
    }
}
