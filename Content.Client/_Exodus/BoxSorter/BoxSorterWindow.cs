using Content.Shared._Exodus.BoxSorter;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths;

namespace Content.Client._Exodus.BoxSorter;

public sealed class BoxSorterWindow : DefaultWindow
{
    public event Action<int?>? OnOtherRouteSelected;
    public event Action<NetEntity, int?>? OnDestinationRouteSelected;

    private readonly BoxContainer _destinations;
    private readonly Dictionary<NetEntity, (BoxContainer Row, Label Name, OptionButton Picker)> _destinationRows = new();
    private readonly OptionButton _otherPicker;

    public BoxSorterWindow()
    {
        Title = Loc.GetString("box-sorter-title");
        MinSize = new Vector2i(420, 320);

        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        var rows = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };

        scroll.AddChild(rows);
        Contents.AddChild(scroll);

        rows.AddChild(BuildHeader());

        _destinations = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            HorizontalExpand = true,
            SeparationOverride = 4,
        };
        rows.AddChild(_destinations);

        var otherRow = BuildRow(Loc.GetString("box-sorter-type-other"), out _otherPicker);
        _otherPicker.OnItemSelected += e => OnOtherRouteSelected?.Invoke(e.Id == -1 ? null : e.Id);
        rows.AddChild(otherRow);
    }

    public void UpdateState(BoxSorterUiState state)
    {
        var destinations = new List<KeyValuePair<NetEntity, string>>(state.Destinations);
        destinations.Sort((a, b) => string.Compare(a.Value, b.Value, StringComparison.Ordinal));

        var alive = new HashSet<NetEntity>();
        foreach (var pair in destinations)
        {
            alive.Add(pair.Key);
            if (!_destinationRows.TryGetValue(pair.Key, out var row))
            {
                var dest = pair.Key;
                var container = BuildRow(pair.Value, out var picker, out var label);
                picker.OnItemSelected += e => OnDestinationRouteSelected?.Invoke(dest, e.Id == -1 ? null : e.Id);
                row = (container, label, picker);
                _destinationRows[pair.Key] = row;
                _destinations.AddChild(container);
            }

            row.Name.Text = pair.Value;
            int? route = state.DestinationRoutes.TryGetValue(pair.Key, out var channel) ? channel : null;
            if (!row.Picker.TrySelectId(route ?? -1))
                row.Picker.SelectId(-1);
        }

        List<NetEntity>? gone = null;
        foreach (var dest in _destinationRows.Keys)
        {
            if (alive.Contains(dest))
                continue;
            gone ??= new List<NetEntity>();
            gone.Add(dest);
        }

        if (gone != null)
        {
            foreach (var dest in gone)
            {
                var row = _destinationRows[dest];
                _destinationRows.Remove(dest);
                _destinations.RemoveChild(row.Row);
            }
        }

        if (!_otherPicker.TrySelectId(state.OtherRoute ?? -1))
            _otherPicker.SelectId(-1);
    }

    private static BoxContainer BuildHeader()
    {
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
        return header;
    }

    private static BoxContainer BuildRow(string name, out OptionButton picker, out Label label)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 8,
        };

        label = new Label
        {
            Text = name,
            HorizontalExpand = true,
            SizeFlagsStretchRatio = 2,
            ClipText = true,
        };
        row.AddChild(label);

        picker = BuildChannelPicker();
        row.AddChild(picker);
        return row;
    }

    private static BoxContainer BuildRow(string name, out OptionButton picker)
    {
        return BuildRow(name, out picker, out _);
    }

    private static OptionButton BuildChannelPicker()
    {
        var options = new OptionButton
        {
            HorizontalExpand = true,
            SizeFlagsStretchRatio = 1,
        };
        options.AddItem(Loc.GetString("box-sorter-unconfigured"), -1);
        for (var channel = BoxSorterComponent.MinChannel; channel <= BoxSorterComponent.MaxChannel; channel++)
            options.AddItem(channel.ToString(), channel);
        return options;
    }
}
