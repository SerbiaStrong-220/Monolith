using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._Exodus.ShipRepair;

public sealed class ShipRepairStationWindow : DefaultWindow
{
    public event Action<ShipRepairStationAction, NetEntity?>? OnCommand;

    private readonly Label _summary;
    private readonly BoxContainer _list;
    private readonly Dictionary<NetEntity, DroneRow> _rows = new();
    private readonly List<NetEntity> _removed = new();
    private readonly Dictionary<ShipRepairStationAction, Button> _groupButtons = new();

    public ShipRepairStationWindow()
    {
        Title = Loc.GetString("ship-repair-station-title");
        MinSize = new Vector2(640, 380);
        SetSize = new Vector2(700, 650);
        var root = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 8 };
        Contents.AddChild(root);
        _summary = new Label();
        root.AddChild(_summary);
        root.AddChild(new Label { Text = Loc.GetString("ship-repair-station-all") });
        var controls = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        root.AddChild(controls);
        var commands = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
        root.AddChild(commands);
        foreach (var action in Enum.GetValues<ShipRepairStationAction>())
        {
            var button = CreateActionButton(action);
            button.OnPressed += _ => OnCommand?.Invoke(action, null);
            (IsOrder(action) ? commands : controls).AddChild(button);
            _groupButtons.Add(action, button);
        }
        var scroll = new ScrollContainer { VerticalExpand = true, HScrollEnabled = false };
        root.AddChild(scroll);
        _list = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical, SeparationOverride = 10 };
        scroll.AddChild(_list);
    }

    public void UpdateState(ShipRepairStationUiState state)
    {
        _summary.Text = Loc.GetString(state.Active ? "ship-repair-station-ready" : "ship-repair-station-unanchored",
            ("count", state.Drones.Count), ("capacity", state.Capacity));
        foreach (var button in _groupButtons.Values)
            button.Disabled = true;
        _removed.Clear();
        foreach (var uid in _rows.Keys)
            _removed.Add(uid);
        foreach (var drone in state.Drones)
        {
            _removed.Remove(drone.Entity);
            if (!_rows.TryGetValue(drone.Entity, out var row))
            {
                row = new DroneRow(drone, (action, uid) => OnCommand?.Invoke(action, uid));
                _rows.Add(drone.Entity, row);
                _list.AddChild(row);
            }
            row.UpdateState(drone, state.Active);
            foreach (var (action, button) in _groupButtons)
                button.Disabled &= !CanCommand(action, drone, state.Active);
        }
        foreach (var uid in _removed)
        {
            _rows[uid].Dispose();
            _rows.Remove(uid);
        }
    }

    private static bool CanCommand(ShipRepairStationAction action, ShipRepairStationDroneInfo drone, bool active)
    {
        return action switch
        {
            ShipRepairStationAction.Eject => drone.Docked && !drone.Enabled,
            ShipRepairStationAction.Disable => drone.Enabled,
            ShipRepairStationAction.Enable => active && drone.Alive && !drone.Enabled && drone.Compatible,
            ShipRepairStationAction.Repair => active && drone.Alive && drone.Enabled && drone.Compatible,
            ShipRepairStationAction.Return => active && drone.Alive && drone.Enabled && !drone.Docked,
            ShipRepairStationAction.Recall => active && drone.Alive && drone.Enabled && !drone.Docked && drone.RecallSeconds == 0,
            _ => false,
        };
    }

    private static string ActionText(ShipRepairStationAction action)
    {
        return Loc.GetString(action switch
        {
            ShipRepairStationAction.Enable => "ship-repair-station-enable",
            ShipRepairStationAction.Disable => "ship-repair-station-disable",
            ShipRepairStationAction.Repair => "ship-repair-station-repair",
            ShipRepairStationAction.Return => "ship-repair-station-return",
            ShipRepairStationAction.Recall => "ship-repair-station-recall",
            ShipRepairStationAction.Eject => "ship-repair-station-eject",
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        });
    }

    private static bool IsOrder(ShipRepairStationAction action)
    {
        return action is ShipRepairStationAction.Repair or ShipRepairStationAction.Return or ShipRepairStationAction.Recall;
    }

    private static Button CreateActionButton(ShipRepairStationAction action)
    {
        return new Button
        {
            Text = ActionText(action),
            HorizontalExpand = true,
            ToolTip = action == ShipRepairStationAction.Recall ? Loc.GetString("ship-repair-station-recall-tooltip") : null,
        };
    }

    private sealed class DroneRow : BoxContainer
    {
        private readonly EntityPrototypeView _sprite;
        private readonly Label _name;
        private readonly Label _status;
        private readonly Label _compatibility;
        private readonly BoxContainer _commands;
        private readonly Dictionary<ShipRepairStationAction, Button> _buttons = new();
        private ShipRepairStationDroneInfo _info;

        public DroneRow(ShipRepairStationDroneInfo info, Action<ShipRepairStationAction, NetEntity> command)
        {
            _info = info;
            Orientation = LayoutOrientation.Vertical;
            var header = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
            AddChild(header);
            _sprite = new EntityPrototypeView { SetSize = new Vector2(48, 48) };
            _sprite.SetPrototype(info.Prototype);
            header.AddChild(_sprite);
            var labels = new BoxContainer { Orientation = LayoutOrientation.Vertical, HorizontalExpand = true };
            header.AddChild(labels);
            _name = new Label { ClipText = true };
            _status = new Label { ClipText = true };
            _compatibility = new Label
            {
                Text = Loc.GetString("ship-repair-station-incompatible"),
                ToolTip = Loc.GetString("ship-repair-station-incompatible"),
                ClipText = true,
            };
            labels.AddChild(_name);
            labels.AddChild(_status);
            var controls = new BoxContainer { Orientation = LayoutOrientation.Horizontal, VerticalAlignment = VAlignment.Center };
            header.AddChild(controls);
            AddChild(_compatibility);
            _commands = new BoxContainer { Orientation = LayoutOrientation.Horizontal };
            AddChild(_commands);
            foreach (var action in Enum.GetValues<ShipRepairStationAction>())
            {
                var button = CreateActionButton(action);
                button.OnPressed += _ => command(action, _info.Entity);
                (IsOrder(action) ? _commands : controls).AddChild(button);
                _buttons.Add(action, button);
            }
        }

        public void UpdateState(ShipRepairStationDroneInfo info, bool active)
        {
            if (_info.Prototype != info.Prototype)
                _sprite.SetPrototype(info.Prototype);
            _info = info;
            _compatibility.Visible = active && info.Alive && !info.Compatible;
            _name.Text = info.Name;
            _name.ToolTip = info.Name;
            _status.Text = Loc.GetString(info.Status switch
            {
                ShipRepairDroneStatus.Off => "ship-repair-station-status-off",
                ShipRepairDroneStatus.Destroyed => "ship-repair-station-status-destroyed",
                ShipRepairDroneStatus.Docked => "ship-repair-station-status-docked",
                ShipRepairDroneStatus.Idle => "ship-repair-station-status-idle",
                ShipRepairDroneStatus.Searching => "ship-repair-station-status-searching",
                ShipRepairDroneStatus.Pathfinding => "ship-repair-station-status-pathfinding",
                ShipRepairDroneStatus.Moving => "ship-repair-station-status-moving",
                ShipRepairDroneStatus.Repairing => "ship-repair-station-status-repairing",
                ShipRepairDroneStatus.Prying => "ship-repair-station-status-prying",
                ShipRepairDroneStatus.Stuck => "ship-repair-station-status-stuck",
                ShipRepairDroneStatus.WaitingForShip => "ship-repair-station-status-waiting",
                ShipRepairDroneStatus.Returning => "ship-repair-station-status-returning",
                ShipRepairDroneStatus.NoReturnPath => "ship-repair-station-status-no-path",
                ShipRepairDroneStatus.ExitBlocked => "ship-repair-station-status-exit-blocked",
                _ => "ship-repair-station-status-idle",
            });
            _status.ToolTip = _status.Text;
            _commands.Visible = info.Enabled;
            foreach (var (action, button) in _buttons)
            {
                button.Disabled = !CanCommand(action, info, active);
                button.Visible = action switch
                {
                    ShipRepairStationAction.Enable => !info.Enabled,
                    ShipRepairStationAction.Disable => info.Enabled,
                    ShipRepairStationAction.Eject => info.Docked && !info.Enabled,
                    _ => info.Enabled,
                };
            }
            var recall = _buttons[ShipRepairStationAction.Recall];
            recall.Text = info.RecallSeconds == 0 ? ActionText(ShipRepairStationAction.Recall) :
                Loc.GetString("ship-repair-station-recall-countdown", ("seconds", info.RecallSeconds));
        }
    }
}
