using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Robust.Shared.Map;

namespace Content.Shared._Mono.ShipRepair;

public abstract partial class SharedShipRepairSystem
{
    [Dependency] private SharedInteractionSystem _repairInteraction = default!;

    private void OnRepairAttempt(Entity<ShipRepairToolComponent> ent, ref DoAfterAttemptEvent<ShipRepairDoAfterEvent> args)
    {
        if (!CanReachRepairLocation(args.Event))
            args.Cancel();
    }

    private bool CanReachRepairLocation(ShipRepairDoAfterEvent ev)
    {
        if (TerminatingOrDeleted(ev.User) || !TryGetEntity(ev.Coordinates.NetEntity, out var grid) ||
            TerminatingOrDeleted(grid.Value))
            return false;

        var coordinates = new EntityCoordinates(grid.Value, ev.Coordinates.Position);
        var range = ev.Args.DistanceThreshold ?? SharedInteractionSystem.InteractionRange;
        if (ev.Target is not { } target)
            return _repairInteraction.InRangeUnobstructed(ev.User, coordinates, range);

        if (TerminatingOrDeleted(target) || !TryComp<TransformComponent>(target, out var targetTransform))
            return false;

        // Keep the original grid-relative position, but allow the clicked wall or wallmount itself.
        return _repairInteraction.InRangeUnobstructed(ev.User, (target, targetTransform), coordinates,
            targetTransform.LocalRotation, range);
    }
}
