using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.DragDrop;
using Content.Shared.Mobs.Components;

namespace Content.Shared._Exodus.ShipRepair;

/// <summary>Client drag feedback; the server validates the drone and its reserved berth on insertion.</summary>
public sealed class SharedShipRepairStationSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShipRepairToolComponent, CanDragEvent>(OnCanDragDrone);
        SubscribeLocalEvent<ShipRepairStationComponent, CanDropTargetEvent>(OnCanDrop, before: new[] { typeof(ClimbSystem) });
    }

    private void OnCanDragDrone(Entity<ShipRepairToolComponent> ent, ref CanDragEvent args)
    {
        // Drones have no Body/Item component to enable drag initiation on the client.
        // The server still checks that the drone is switched off before loading it.
        if (HasComp<MobStateComponent>(ent))
            args.Handled = true;
    }

    private void OnCanDrop(Entity<ShipRepairStationComponent> ent, ref CanDropTargetEvent args)
    {
        args.Handled = true;
        args.CanDrop = HasComp<ShipRepairToolComponent>(args.Dragged) && HasComp<MobStateComponent>(args.Dragged);
    }
}
