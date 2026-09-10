using System.Numerics;
using Content.Server.DoAfter;
using Content.Server.Popups;
using Content.Shared._Exodus.Territory;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

public sealed class GrowTerritoryCoreSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _factory = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly DoAfterSystem _doAfter = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TerritoryCoreSystem _cores = default!;
    [Dependency] private readonly TerritoryClaimIntegritySystem _integrity = default!;
    [Dependency] private readonly TerritoryClaimRulesSystem _rules = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GrowTerritoryCoreComponent, GrowTerritoryCoreActionEvent>(OnGrow);
        SubscribeLocalEvent<GrowTerritoryCoreComponent, GrowTerritoryCoreDoAfterEvent>(OnGrowFinished);
    }

    private void OnGrow(Entity<GrowTerritoryCoreComponent> ent, ref GrowTerritoryCoreActionEvent args)
    {
        if (args.Handled || !TryValidate(ent, args.Target, out var position))
            return;

        var ev = new GrowTerritoryCoreDoAfterEvent { Coordinates = GetNetCoordinates(position) };
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ent.Owner, ent.Comp.GrowDelay, ev, ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
            BlockDuplicate = true,
            CancelDuplicate = false,
        });
    }

    private void OnGrowFinished(Entity<GrowTerritoryCoreComponent> ent, ref GrowTerritoryCoreDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled ||
            !TryGetEntity(args.Coordinates.NetEntity, out var coordinateEntity) ||
            coordinateEntity is not { } coordinateUid ||
            !TryValidate(ent, new EntityCoordinates(coordinateUid, args.Coordinates.Position), out var position))
        {
            return;
        }

        args.Handled = true;
        var core = Spawn(ent.Comp.Core, position);
        if (!TryComp<TerritoryCoreComponent>(core, out _) ||
            !TryComp<TerritoryBannerComponent>(core, out _) || Transform(core).Anchored)
        {
            Log.Error($"Territory core {ent.Comp.Core} must spawn unanchored with TerritoryCore and TerritoryBanner.");
            QueueDel(core);
            return;
        }

        // Pass the actor through the existing banner path, including capture logs and cooldowns.
        EnsureComp<PendingTerritoryClaimActorComponent>(core).Actor = ent.Owner;
        if (!_transform.AnchorEntity((core, Transform(core))) || !_cores.IsActive(core))
        {
            QueueDel(core);
            _popup.PopupEntity(Loc.GetString("exodus-hive-grow-failed"), ent, ent);
        }
    }

    private bool TryValidate(Entity<GrowTerritoryCoreComponent> ent, EntityCoordinates coordinates, out EntityCoordinates position)
    {
        position = default;
        if (TerminatingOrDeleted(ent) || EntityManager.IsQueuedForDeletion(ent) || !_mobState.IsAlive(ent) ||
            _container.IsEntityOrParentInContainer(ent) || !coordinates.IsValid(EntityManager))
        {
            return false;
        }

        if (_transform.GetGrid(coordinates) is not { } grid || TerminatingOrDeleted(grid) || EntityManager.IsQueuedForDeletion(grid) ||
            Transform(ent).GridUid != grid ||
            !TryComp<MapGridComponent>(grid, out var mapGrid) ||
            !TryComp<GridTerritoryComponent>(grid, out var territory) || !territory.Claimable)
        {
            return Deny(ent, "exodus-hive-grow-no-territory");
        }

        if (territory.ControllingFaction != null ||
            territory.ActiveClaimBanner is { } banner && !TerminatingOrDeleted(banner))
        {
            return Deny(ent, "exodus-hive-grow-claimed");
        }

        position = _transform.WithEntityId(coordinates, grid);
        var userPosition = _transform.WithEntityId(Transform(ent).Coordinates, grid);
        if (Vector2.DistanceSquared(position.Position, userPosition.Position) > ent.Comp.Range * ent.Comp.Range ||
            !_interaction.InRangeUnobstructed(ent.Owner, position, ent.Comp.Range) ||
            !_cores.IsFreeFloor((grid, mapGrid), position))
        {
            return Deny(ent, "exodus-hive-grow-blocked");
        }

        if (!_prototype.TryIndex(ent.Comp.Core, out var prototype) ||
            !prototype.TryGetComponent<TerritoryBannerComponent>(out var claim, _factory))
        {
            Log.Error($"Invalid territory core prototype: {ent.Comp.Core}");
            return false;
        }

        if (!_rules.CanStartClaim(claim.Faction, out var message))
        {
            _popup.PopupEntity(message, ent, ent);
            return false;
        }

        if (ent.Comp.RequireRepairIntegrity && !_integrity.CanAnchorClaimBanner((grid, territory)))
            return Deny(ent, "grid-territory-claim-low-integrity");

        return true;
    }

    private bool Deny(Entity<GrowTerritoryCoreComponent> ent, LocId message)
    {
        _popup.PopupEntity(Loc.GetString(message), ent, ent);
        return false;
    }
}
