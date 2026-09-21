using Content.Shared._Exodus.ShipRepair;
using Content.Shared.ActionBlocker;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Verbs;

namespace Content.Server._Exodus.ShipRepair;

public sealed partial class ShipRepairDroneSystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;

    private void OnStationLoadVerbs(Entity<ShipRepairStationComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || TerminatingOrDeleted(ent))
            return;

        var user = args.User;
        var category = new VerbCategory("ship-repair-station-load-category", "/Textures/Interface/VerbIcons/insert.svg.192dpi.png");
        foreach (var drone in _lookup.GetEntitiesInRange<ShipRepairDroneComponent>(
                     _transform.GetMapCoordinates(ent), SharedInteractionSystem.InteractionRange))
        {
            if (!CanLoadDrone(ent, drone) || !_interaction.InRangeUnobstructed(user, drone.Owner) ||
                !_interaction.InRangeUnobstructed(drone.Owner, ent.Owner))
                continue;

            var stationUid = ent.Owner;
            var droneUid = drone.Owner;
            args.Verbs.Add(new Verb
            {
                Category = category,
                Text = Identity.Name(droneUid, EntityManager),
                // Keeps identically named, unregistered drones as separate selectable entries.
                IconEntity = GetNetEntity(droneUid),
                Act = () => LoadDrone(stationUid, user, droneUid),
            });
        }
    }

    private bool CanLoadDrone(Entity<ShipRepairStationComponent> station, Entity<ShipRepairDroneComponent> drone)
    {
        return !TerminatingOrDeleted(station) && !EntityManager.IsQueuedForDeletion(station) &&
               !TerminatingOrDeleted(drone) && !EntityManager.IsQueuedForDeletion(drone) &&
               !drone.Comp.Enabled && !drone.Comp.Phased && !_containers.IsEntityInContainer(drone) &&
               _containers.TryGetContainer(station, station.Comp.ContainerId, out var container) &&
               _containers.CanInsert(drone.Owner, container);
    }

    private void LoadDrone(EntityUid stationUid, EntityUid user, EntityUid droneUid)
    {
        // A context-menu callback can run after movement, deletion or another player's insertion.
        if (TerminatingOrDeleted(user) || TerminatingOrDeleted(stationUid) ||
            !_stationQuery.TryGetComponent(stationUid, out var station))
            return;
        var ent = new Entity<ShipRepairStationComponent>(stationUid, station);
        if (!TryLoadDrone(ent, user, droneUid))
            _popup.PopupEntity(Loc.GetString("ship-repair-station-load-failed"), stationUid, user);
        UpdateStationUi(ent);
    }

    private bool TryLoadDrone(Entity<ShipRepairStationComponent> station, EntityUid user, EntityUid droneUid)
    {
        if (!_droneQuery.TryGetComponent(droneUid, out var comp))
            return false;
        var drone = new Entity<ShipRepairDroneComponent>(droneUid, comp);
        if (!CanLoadDrone(station, drone) || !_actionBlocker.CanInteract(user, station.Owner) ||
            !_actionBlocker.CanInteract(user, droneUid) ||
            !_interaction.InRangeUnobstructed(user, station.Owner) || !_interaction.InRangeUnobstructed(user, droneUid) ||
            !_interaction.InRangeUnobstructed(droneUid, station.Owner) ||
            !_containers.TryGetContainer(station, station.Comp.ContainerId, out var container))
            return false;
        return _containers.Insert(droneUid, container);
    }
}
