using Content.Shared._Exodus.BoxSorter;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client._Exodus.BoxSorter;

public sealed class BoxSorterWindow : DefaultWindow
{
    public event Action<int?>? OnOtherRouteSelected;
    public event Action<string, int?>? OnDestinationRouteSelected;

    private readonly BoxContainer _rows;

    public BoxSorterWindow()
    {
        Title = Loc.GetString("box-sorter-title");
        MinSize = new Vector2i(420, 320);

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _rows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };

        scroll.AddChild(_rows);
        Contents.AddChild(scroll);
    }

    public void UpdateState(BoxSorterUiState state)
    {
        _rows.DisposeAllChildren();

        var header = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };
        header.AddChild(new Label
        {
            Text = Loc.GetString("box-sorter-destination-header"),
            HorizontalExpand = true,
            SizeFlagsStretchRatio = 2,
        });
        header.AddChild(new Label
        {
            Text = Loc.GetString("box-sorter-channel-header"),
            HorizontalExpand = true,
            SizeFlagsStretchRatio = 1,
        });
        _rows.AddChild(header);

        var destinations = new List<KeyValuePair<string, string>>(state.Destinations);
        destinations.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.Ordinal));
        foreach (var pair in destinations)
        {
            var dest = pair.Key;
            int? route = state.DestinationRoutes.TryGetValue(dest, out var channel) ? channel : null;
            AddRow(pair.Value, route, c => OnDestinationRouteSelected?.Invoke(dest, c));
        }

        AddRow(Loc.GetString("box-sorter-type-other"), state.OtherRoute,
            c => OnOtherRouteSelected?.Invoke(c));
    }

    private void AddRow(string name, int? current, Action<int?> onPick)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };

        row.AddChild(new Label
        {
            Text = name,
            HorizontalExpand = true,
            SizeFlagsStretchRatio = 2,
            ClipText = true,
        });

        var options = new OptionButton
        {
            HorizontalExpand = true,
            SizeFlagsStretchRatio = 1,
        };
        options.AddItem(Loc.GetString("box-sorter-unconfigured"), -1);
        for (var channel = BoxSorterComponent.MinChannel; channel <= BoxSorterComponent.MaxChannel; channel++)
            options.AddItem(channel.ToString(), channel);

        if (!options.TrySelectId(current ?? -1))
            options.SelectId(-1);
        options.OnItemSelected += e => onPick(e.Id == -1 ? null : e.Id);

        row.AddChild(options);
        _rows.AddChild(row);
    }
}
