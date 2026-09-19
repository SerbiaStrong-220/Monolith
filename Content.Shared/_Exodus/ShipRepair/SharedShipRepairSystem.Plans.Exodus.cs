using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Damage;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Prototypes;
using Content.Shared.Repairable;
using Content.Shared.SubFloor;
using Content.Shared.Wall;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.ShipRepair;

public abstract partial class SharedShipRepairSystem
{
    [Dependency] private DamageableSystem _repairDamage = default!;

    private EntityQuery<MapGridComponent> _repairGridQuery;
    private EntityQuery<DamageableComponent> _repairDamageQuery;
    private EntityQuery<MobStateComponent> _repairMobQuery;
    private EntityQuery<TransformComponent> _repairTransformQuery;
    private EntityQuery<PhysicsComponent> _repairBodyQuery;
    private EntityQuery<FixturesComponent> _repairFixturesQuery;
    private EntityQuery<ItemComponent> _repairItemQuery;
    private EntityQuery<ShipRepairDebrisComponent> _repairDebrisQuery;
    private EntityQuery<ShipRepairableComponent> _repairableQuery;
    private EntityQuery<MetaDataComponent> _repairMetadataQuery;
    private EntityQuery<WallMountComponent> _repairWallMountQuery;
    private readonly HashSet<EntityUid> _repairObstructions = new();

    private void InitRepairPlans()
    {
        _repairGridQuery = GetEntityQuery<MapGridComponent>();
        _repairDamageQuery = GetEntityQuery<DamageableComponent>();
        _repairMobQuery = GetEntityQuery<MobStateComponent>();
        _repairTransformQuery = GetEntityQuery<TransformComponent>();
        _repairBodyQuery = GetEntityQuery<PhysicsComponent>();
        _repairFixturesQuery = GetEntityQuery<FixturesComponent>();
        _repairItemQuery = GetEntityQuery<ItemComponent>();
        _repairDebrisQuery = GetEntityQuery<ShipRepairDebrisComponent>();
        _repairableQuery = GetEntityQuery<ShipRepairableComponent>();
        _repairMetadataQuery = GetEntityQuery<MetaDataComponent>();
        _repairWallMountQuery = GetEntityQuery<WallMountComponent>();
    }

    /// <summary>Both whole-grid and individual prototype restrictions apply to every operation.</summary>
    public bool CanRepairGrid(EntityUid tool, EntityUid grid)
    {
        return !TerminatingOrDeleted(tool) && !TerminatingOrDeleted(grid) && _repairGridQuery.HasComponent(grid) &&
               TryComp<ShipRepairDataComponent>(grid, out var data) && data.ChunkSize > 0 &&
               (!TryComp<ShipRepairRestrictComponent>(grid, out var restriction) ||
                !_whitelist.IsWhitelistFail(restriction.ToolWhitelist, tool));
    }

    /// <summary>Cheap discovery without spatial queries or a copy of the target's damage.</summary>
    public bool NeedsSnapshotRepair(Entity<ShipRepairDataComponent> grid, ShipRepairTarget target)
    {
        if (grid.Comp.ChunkSize <= 0 || !TryGetChunk(grid.Comp, target.Tile, out var chunk))
            return false;

        if (target.EntityId is not { } id)
        {
            var relative = GetRelativeIndices(target.Tile, grid.Comp.ChunkSize);
            var stored = chunk.Tiles[relative.X + relative.Y * grid.Comp.ChunkSize];
            return stored != Tile.Empty.TypeId && _repairGridQuery.TryGetComponent(grid, out var mapGrid) &&
                   _map.GetTileRef(grid, mapGrid, target.Tile).Tile.TypeId != stored;
        }

        if (!chunk.Entities.TryGetValue(id, out var spec))
            return false;

        if (spec.OriginalEntity is not { } netOriginal || !TryGetEntity(netOriginal, out var original) ||
            TerminatingOrDeleted(original))
            return true;

        if (!_repairTransformQuery.TryGetComponent(original.Value, out var xform))
            return false;
        if (xform.ParentUid == grid.Owner && xform.Anchored &&
            Vector2.DistanceSquared(xform.LocalPosition, spec.LocalPosition) < 0.01f &&
            !_repairMobQuery.HasComponent(original.Value) &&
            _repairDamageQuery.TryGetComponent(original.Value, out var damage) && damage.TotalDamage > 0)
            return true;

        var ev = new ShipRepairReinstateQueryEvent(true);
        RaiseLocalEvent(original.Value, ref ev);
        return ev.Handled && ev.Repairable;
    }

    /// <summary>
    /// Quotes one operation using normal SRD times and restrictions. Navigation may defer mobile occupancy
    /// checks until arrival; completion always checks them, including the repairer's own body.
    /// </summary>
    public bool TryPlanRepair(Entity<ShipRepairToolComponent> tool, Entity<ShipRepairDataComponent> grid,
        ShipRepairTarget target, bool healDamage, out ShipRepairWork work, bool checkMobileObstructions = true,
        bool checkTileSupport = true)
    {
        work = default!;
        if (!CanRepairGrid(tool, grid) || !_repairGridQuery.TryGetComponent(grid, out var mapGrid) ||
            !NeedsSnapshotRepair(grid, target) ||
            !TryGetChunk(grid.Comp, target.Tile, out var chunk))
            return false;

        if (target.EntityId is not { } id)
        {
            if (!tool.Comp.EnableTileRepair || checkTileSupport && !CanRestoreRepairTile((grid, mapGrid), target.Tile))
                return false;

            var relative = GetRelativeIndices(target.Tile, grid.Comp.ChunkSize);
            work = new ShipRepairWork
            {
                Target = target,
                Operation = ShipRepairOperation.Tile,
                Stage = ShipRepairStage.Floor,
                Position = _map.TileCenterToVector(grid, mapGrid, target.Tile),
                Duration = TimeSpan.FromSeconds(tool.Comp.TileRepairTime * tool.Comp.RepairTimeMultiplier),
                Cost = tool.Comp.TileRepairCost,
                TileType = chunk.Tiles[relative.X + relative.Y * grid.Comp.ChunkSize],
            };
            return true;
        }

        if (!tool.Comp.EnableEntityRepair || !chunk.Entities.TryGetValue(id, out var spec) ||
            !TryGetRepairPrototype(tool, grid.Comp, spec, out var prototype, out var repairable))
            return false;

        var operation = ShipRepairOperation.Restore;
        EntityUid? original = null;
        DamageSpecifier? damage = null;
        if (spec.OriginalEntity is { } netOriginal && TryGetEntity(netOriginal, out original) &&
            !TerminatingOrDeleted(original))
        {
            var xform = Transform(original.Value);
            if (healDamage && xform.ParentUid == grid.Owner && xform.Anchored &&
                Vector2.DistanceSquared(xform.LocalPosition, spec.LocalPosition) < 0.01f &&
                !HasComp<MobStateComponent>(original) &&
                TryComp<DamageableComponent>(original, out var damageable) && damageable.TotalDamage > 0)
            {
                if (TryComp<ShipRepairableRestrictComponent>(original, out var restrict) &&
                    _whitelist.IsWhitelistFail(restrict.ToolWhitelist, tool))
                    return false;

                operation = ShipRepairOperation.Heal;
                damage = new DamageSpecifier(damageable.Damage);
            }
            else
            {
                var ev = new ShipRepairReinstateQueryEvent(true);
                RaiseLocalEvent(original.Value, ref ev);
                if (!ev.Handled || !ev.Repairable)
                    return false;
            }
        }

        if (original != null && TerminatingOrDeleted(original))
            original = null;

        if (operation == ShipRepairOperation.Restore &&
            IsRepairPositionOccupied(grid, chunk, id, spec, prototype, checkMobileObstructions))
            return false;

        prototype.TryGetComponent<WallMountComponent>(out var wallMount, Factory);
        work = new ShipRepairWork
        {
            Target = target,
            Operation = operation,
            Position = spec.LocalPosition,
            Duration = TimeSpan.FromSeconds(repairable.RepairTime * tool.Comp.RepairTimeMultiplier),
            Cost = repairable.RepairCost,
            Original = original,
            Damage = damage,
            Prototype = prototype.ID,
            Rotation = spec.Rotation,
            Stage = GetRepairStage(prototype),
            Underfloor = prototype.HasComponent<SubFloorHideComponent>(Factory),
            WallMountArc = wallMount?.Arc,
            WallMountDirection = wallMount?.Direction ?? Angle.Zero,
        };
        return true;
    }

    /// <summary>
    /// Adds all actual work in a square aligned to the grid, independently of snapshot chunk boundaries.
    /// A radius of one is 3x3. Callers may filter accessibility and reservations before starting their DoAfter.
    /// </summary>
    public void PlanRepairArea(Entity<ShipRepairToolComponent> tool, Entity<ShipRepairDataComponent> grid,
        Vector2i center, int radius, bool healDamage, ShipRepairPlan plan, bool checkMobileObstructions = true)
    {
        if (plan.Grid != grid.Owner || plan.Revision != grid.Comp.Revision || radius < 0 ||
            !TryComp<MapGridComponent>(grid, out var mapGrid))
            return;

        for (var y = center.Y - radius; y <= center.Y + radius; y++)
        for (var x = center.X - radius; x <= center.X + radius; x++)
        {
            var tile = new Vector2i(x, y);
            if (TryPlanRepair(tool, grid, new ShipRepairTarget(tile), healDamage, out var floor,
                    checkMobileObstructions, checkTileSupport: false))
                plan.Work.Add(floor);

            if (!TryGetChunk(grid.Comp, tile, out var chunk))
                continue;

            foreach (var (id, spec) in chunk.Entities)
            {
                if (_map.LocalToTile(grid.Owner, mapGrid,
                        new EntityCoordinates(grid, spec.LocalPosition)) != tile)
                    continue;

                if (TryPlanRepair(tool, grid, new ShipRepairTarget(tile, id), healDamage, out var entity, checkMobileObstructions))
                    plan.Work.Add(entity);
            }
        }
        PrepareConnectedRepairPlan(grid, plan);
    }

    public TimeSpan GetRepairDuration(ShipRepairPlan plan, float throughput = 1f)
    {
        var duration = TimeSpan.Zero;
        foreach (var work in plan.Work)
            duration += work.Duration;
        return duration / Math.Max(0.01f, throughput);
    }

    /// <summary>
    /// Commits only the quoted operation. Rechecks snapshot, restrictions, original and occupied space.
    /// Charge consumption belongs here, so partially invalidated batches never charge for skipped work.
    /// </summary>
    public bool TryCompleteRepair(Entity<ShipRepairToolComponent> tool, EntityUid user,
        ShipRepairPlan plan, ShipRepairWork work)
    {
        if (!_net.IsServer || !TryComp<ShipRepairDataComponent>(plan.Grid, out var data) ||
            data.Revision != plan.Revision || !CanRepairGrid(tool, plan.Grid) ||
            _charges.HasInsufficientCharges(tool, work.Cost) ||
            !TryPlanRepair(tool, (plan.Grid, data), work.Target, work.Operation == ShipRepairOperation.Heal, out var current) ||
            current.Operation != work.Operation || current.Original != work.Original)
            return false;

        switch (work.Operation)
        {
            case ShipRepairOperation.Tile:
                if (!TryRepairTileTile((plan.Grid, data), work.Target.Tile))
                    return false;
                break;
            case ShipRepairOperation.Restore:
                if (!TryGetChunk(data, work.Target.Tile, out var chunk) || work.Target.EntityId is not { } id ||
                    !chunk.Entities.TryGetValue(id, out var spec) ||
                    NeedsSnapshotRepair((plan.Grid, data), new ShipRepairTarget(work.Target.Tile)))
                    return false;
                if (!TryRestoreSnapshotEntity(tool, (plan.Grid, data), work.Target.Tile, id, spec))
                    return false;
                break;
            case ShipRepairOperation.Heal:
                if (work.Original is not { } original || work.Damage == null ||
                    !TryComp<DamageableComponent>(original, out var damageable))
                    return false;

                var healing = new DamageSpecifier();
                foreach (var (type, amount) in work.Damage.DamageDict)
                {
                    if (amount > 0 && damageable.Damage.DamageDict.TryGetValue(type, out var remaining) && remaining > 0)
                        healing.DamageDict[type] = -(amount < remaining ? amount : remaining);
                }

                if (healing.Empty || _repairDamage.TryChangeDamage(original, healing, true, false, origin: user) == null)
                    return false;

                if (TryComp<RepairableComponent>(original, out var repairable))
                {
                    var ev = new RepairedEvent((original, repairable), user);
                    RaiseLocalEvent(original, ref ev);
                }
                break;
        }

        _charges.UseCharges(tool, work.Cost);
        return true;
    }

    private bool TryGetRepairPrototype(EntityUid tool, ShipRepairDataComponent data, ShipRepairEntitySpecifier spec,
        out EntityPrototype prototype, out ShipRepairableComponent repairable)
    {
        prototype = default!;
        repairable = default!;
        if (spec.ProtoIndex < 0 || spec.ProtoIndex >= data.EntityPalette.Count ||
            !_proto.TryIndex(data.EntityPalette[spec.ProtoIndex], out var resolved) ||
            !resolved.TryGetComponent<ShipRepairableComponent>(out var repair, Factory) ||
            resolved.TryGetComponent<ShipRepairableRestrictComponent>(out var restrict, Factory) &&
            _whitelist.IsWhitelistFail(restrict.ToolWhitelist, tool))
            return false;

        // Also honor repair replacements for snapshots captured before a prototype was corrected.
        if (repair.RepairTo is { } replacement && replacement.Id != resolved.ID)
        {
            if (!_proto.TryIndex(replacement, out resolved) ||
                !resolved.TryGetComponent<ShipRepairableComponent>(out repair, Factory) ||
                resolved.TryGetComponent<ShipRepairableRestrictComponent>(out var replacementRestriction, Factory) &&
                _whitelist.IsWhitelistFail(replacementRestriction.ToolWhitelist, tool))
                return false;
        }

        prototype = resolved;
        repairable = repair;
        return true;
    }

    private FixturesComponent? GetRepairCollisionFixtures(EntityPrototype prototype)
    {
        if (!prototype.TryGetComponent<PhysicsComponent>(out var body, Factory) ||
            !prototype.TryGetComponent<FixturesComponent>(out var fixtures, Factory))
            return null;

        var canCollide = body.CanCollide;
        // Pipes keep their loose-item fixtures, but disable collision when anchored.
        // Match CollideOnAnchorSystem's startup state instead of treating these fixtures as solid.
        if (prototype.TryGetComponent<CollideOnAnchorComponent>(out var onAnchor, Factory))
        {
            var anchored = prototype.TryGetComponent<TransformComponent>(out var xform, Factory) && xform.Anchored;
            canCollide = anchored ? onAnchor.Enable : !onAnchor.Enable;
        }

        return canCollide ? fixtures : null;
    }

    private bool IsRepairPositionOccupied(Entity<ShipRepairDataComponent> grid, ShipRepairChunk chunk, int id,
        ShipRepairEntitySpecifier spec, EntityPrototype prototype, bool checkMobileObstructions)
    {
        if (!TryComp<MapGridComponent>(grid, out var mapGrid))
            return true;

        var tile = _map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, spec.LocalPosition));
        CollectRepairSnapshotOccupants(grid, mapGrid, tile, chunk);
        if (_repairOccupiedSlots.Contains(id))
            return true;

        var fixtures = GetRepairCollisionFixtures(prototype);
        var wallMounted = prototype.HasComponent<WallMountComponent>(Factory);
        var origin = _transform.ToMapCoordinates(new EntityCoordinates(grid, spec.LocalPosition));
        var placement = new Robust.Shared.Physics.Transform(origin.Position, _transform.GetWorldRotation(grid) + spec.Rotation);
        foreach (var uid in _repairTileOccupants)
        {
            // The snapshot can deliberately contain multiple overlapping structures. Wall-mounted devices
            // can also coexist with their support even when that support was manually replaced.
            if (!_repairMobQuery.HasComponent(uid) && (_repairSnapshotOccupants.ContainsKey(uid) ||
                wallMounted || _repairWallMountQuery.HasComponent(uid) || IsMovableRepairDebris(uid)))
                continue;

            if (fixtures == null || !_repairBodyQuery.TryGetComponent(uid, out var body) || !body.CanCollide ||
                !_repairFixturesQuery.TryGetComponent(uid, out var other))
                continue;
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (RepairFixtureIntersectsBody(fixture, placement, uid, other))
                    return true;
            }
        }

        if (!checkMobileObstructions || fixtures == null)
            return false;

        return FindMobileRepairObstructions(grid, spec, fixtures, null);
    }

    /// <summary>Adds mobile occupants of a proposed reconstruction, allowing a caller to ask them to move.</summary>
    public void GetRepairObstructions(Entity<ShipRepairDataComponent> grid, ShipRepairWork work,
        HashSet<EntityUid> obstructions)
    {
        if (TerminatingOrDeleted(grid) || grid.Comp.ChunkSize <= 0 || work.Operation != ShipRepairOperation.Restore ||
            work.Target.EntityId is not { } id || !TryGetChunk(grid.Comp, work.Target.Tile, out var chunk) ||
            !chunk.Entities.TryGetValue(id, out var spec) || spec.ProtoIndex < 0 ||
            spec.ProtoIndex >= grid.Comp.EntityPalette.Count ||
            !_proto.TryIndex(grid.Comp.EntityPalette[spec.ProtoIndex], out var prototype) ||
            GetRepairCollisionFixtures(prototype) is not { } fixtures)
            return;

        FindMobileRepairObstructions(grid, spec, fixtures, obstructions);
    }

    private bool FindMobileRepairObstructions(EntityUid grid, ShipRepairEntitySpecifier spec,
        FixturesComponent fixtures, HashSet<EntityUid>? obstructions, HashSet<EntityUid>? debris = null)
    {
        var blocked = false;
        var coordinates = _transform.ToMapCoordinates(new EntityCoordinates(grid, spec.LocalPosition));
        var rotation = _transform.GetWorldRotation(grid) + spec.Rotation;
        var placement = new Robust.Shared.Physics.Transform(coordinates.Position, rotation);
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            // Do not rebuild a solid object around mobile occupants. Static neighbors are checked by tile
            // above: full-tile wall shapes touch each other and an overlap query would reject those contacts.
            _repairObstructions.Clear();
            _lookup.GetEntitiesIntersecting(coordinates.MapId, fixture.Shape, placement, _repairObstructions,
                LookupFlags.Dynamic);
            foreach (var uid in _repairObstructions)
            {
                if (TerminatingOrDeleted(uid) || !_repairBodyQuery.TryGetComponent(uid, out var body) ||
                    !body.CanCollide || !_repairFixturesQuery.TryGetComponent(uid, out var other) ||
                    !RepairFixtureIntersectsBody(fixture, placement, uid, other))
                    continue;

                if (IsMovableRepairDebris(uid))
                {
                    debris?.Add(uid);
                    continue;
                }
                if (obstructions == null)
                    return true;
                obstructions.Add(uid);
                blocked = true;
            }
        }
        return blocked;
    }

    /// <summary>Shared publication path for both handheld and automated reconstruction.</summary>
    private bool TryRestoreSnapshotEntity(EntityUid tool, Entity<ShipRepairDataComponent> grid, Vector2i tile, int id,
        ShipRepairEntitySpecifier spec)
    {
        var revision = grid.Comp.Revision;
        if (!CanRepairGrid(tool, grid) || !TryGetChunk(grid.Comp, tile, out var chunk) ||
            !chunk.Entities.TryGetValue(id, out var current) || !ReferenceEquals(current, spec) ||
            !TryGetRepairPrototype(tool, grid.Comp, spec, out var prototype, out _) ||
            IsRepairPositionOccupied(grid, chunk, id, spec, prototype, true) ||
            !TryMoveRepairDebris(grid, tile, chunk, spec, prototype))
            return false;

        // Unanchoring and moving a remnant raises events; recheck before publishing a new entity.
        if (!CanRepairGrid(tool, grid) || grid.Comp.Revision != revision ||
            !TryGetChunk(grid.Comp, tile, out chunk) || !chunk.Entities.TryGetValue(id, out current) ||
            !ReferenceEquals(current, spec) || IsRepairPositionOccupied(grid, chunk, id, spec, prototype, true))
            return false;

        var spawned = Spawn(prototype.ID, new EntityCoordinates(grid, spec.LocalPosition));
        _transform.SetLocalRotation(spawned, spec.Rotation);
        spec.OriginalEntity = GetNetEntity(spawned);
        RaiseNetworkEvent(new RepairEntityMessage(GetNetEntity(grid), tile, id, spec, grid.Comp.Revision));
        return true;
    }
}
