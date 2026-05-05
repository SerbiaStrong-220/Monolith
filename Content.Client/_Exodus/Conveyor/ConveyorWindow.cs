using Content.Shared._Exodus.Conveyor;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Exodus.Conveyor;

public sealed class ConveyorWindow : DefaultWindow
{
    public event Action<ConveyorSpeedTier>? OnTierSelected;

    public ConveyorWindow()
    {
        Title = Loc.GetString("ui-conveyor-speed-label");
        MinSize = new Robust.Shared.Maths.Vector2i(240, 90);

        var container = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
            Margin = new Robust.Shared.Maths.Thickness(8),
        };

        var tiers = Enum.GetValues<ConveyorSpeedTier>();
        for (var i = 0; i < tiers.Length; i++)
        {
            var tier = tiers[i];
            var button = new Button
            {
                Text = Loc.GetString($"ui-conveyor-speed-{tier.ToString().ToLowerInvariant()}"),
                HorizontalExpand = true,
            };

            // Style classes for button grouping
            if (i == 0)
                button.AddStyleClass("OpenRight");
            else if (i == tiers.Length - 1)
                button.AddStyleClass("OpenLeft");
            else
                button.AddStyleClass("OpenBoth");

            button.OnPressed += _ => OnTierSelected?.Invoke(tier);
            container.AddChild(button);
        }

        Contents.AddChild(container);
    }
}
