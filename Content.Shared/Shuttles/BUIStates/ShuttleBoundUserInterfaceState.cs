using Content.Shared.Shuttles.UI.MapObjects;
using Content.Shared.Exodus.ShipShields; // Exodus - shield health
using Robust.Shared.Serialization;

namespace Content.Shared.Shuttles.BUIStates;

[Serializable, NetSerializable]
public sealed class ShuttleBoundUserInterfaceState : BoundUserInterfaceState
{
    public NavInterfaceState NavState;
    public ShuttleMapInterfaceState MapState;
    public DockingInterfaceState DockState;
    public ShipShieldState? ShieldState; // Exodus - shield health

    public ShuttleBoundUserInterfaceState(
        NavInterfaceState navState,
        ShuttleMapInterfaceState mapState,
        DockingInterfaceState dockState,
        ShipShieldState? shieldState = null) // Exodus - shield health
    {
        NavState = navState;
        MapState = mapState;
        DockState = dockState;
        ShieldState = shieldState; // Exodus - shield health
    }
}
