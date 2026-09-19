using Content.Shared._Exodus.ShipRepair;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Exodus.ShipRepair;

[UsedImplicitly]
public sealed class ShipRepairStationBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    private ShipRepairStationWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<ShipRepairStationWindow>();
        _window.OnCommand += (action, drone) => SendMessage(new ShipRepairStationMessage(action, drone));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is ShipRepairStationUiState station)
            _window?.UpdateState(station);
    }
}
