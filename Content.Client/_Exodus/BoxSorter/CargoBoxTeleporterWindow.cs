using Content.Shared._Exodus.BoxSorter;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client._Exodus.BoxSorter;

public sealed class CargoBoxTeleporterWindow : DefaultWindow
{
    public event Action<int>? OnChannelSelected;

    public CargoBoxTeleporterWindow()
    {
        Title = Loc.GetString("cargo-teleporter-title");
        MinSize = new Vector2i(220, 300);

        var container = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            VerticalExpand = true,
            SeparationOverride = 4,
            Margin = new Thickness(8),
        };

        for (var channel = BoxSorterComponent.MinChannel; channel <= BoxSorterComponent.MaxChannel; channel++)
        {
            var picked = channel;
            var button = new Button
            {
                Text = Loc.GetString("cargo-teleporter-channel-button", ("channel", picked)),
                HorizontalExpand = true,
            };
            button.OnPressed += _ => OnChannelSelected?.Invoke(picked);
            container.AddChild(button);
        }

        Contents.AddChild(container);
    }

    public void UpdateState(CargoBoxTeleporterUiState state)
    {
        Title = Loc.GetString("cargo-teleporter-window-title", ("channel", state.Channel));
    }
}
