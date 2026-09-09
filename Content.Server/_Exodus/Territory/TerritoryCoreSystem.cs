using System.Numerics;
using Content.Shared._Exodus.Territory;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Tag;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Territory;

public sealed class TerritoryCoreSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly AnchorableSystem _anchorable = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TagSystem _tag = default!;

    private EntityQuery<GridTerritoryComponent> _territoryQuery;
    private EntityQuery<TerritoryCoreComponent> _coreQuery;
    private EntityQuery<TerritoryBannerComponent> _bannerQuery;
    private EntityQuery<TransformComponent> _transformQuery;

    public override void Initialize()
    {
        base.Initialize();
        _territoryQuery = GetEntityQuery<GridTerritoryComponent>();
        _coreQuery = GetEntityQuery<TerritoryCoreComponent>();
        _bannerQuery = GetEntityQuery<TerritoryBannerComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<GridTerritoryControllerChangedEvent>(OnControllerChanged);
        SubscribeLocalEvent<TerritoryCoreComponent, MapInitEvent>(OnCoreMapInit);
        SubscribeLocalEvent<TerritoryCoreComponent, ComponentShutdown>(OnCoreShutdown);
        SubscribeLocalEvent<TerritoryCoreComponent, ExaminedEvent>(OnExamined);
    }

    public bool IsActive(EntityUid core)
    {
        return !TerminatingOrDeleted(core) && !EntityManager.IsQueuedForDeletion(core) && _coreQuery.HasComponent(core) &&
               _bannerQuery.TryGetComponent(core, out var banner) &&
               _transformQuery.TryGetComponent(core, out var xform) && xform.Anchored &&
               xform.GridUid is { } grid && !TerminatingOrDeleted(grid) && !EntityManager.IsQueuedForDeletion(grid) &&
               _territoryQuery.TryGetComponent(grid, out var territory) &&
               territory.Claimable && territory.ActiveClaimBanner == core && territory.ControllingFaction == banner.Faction;
    }

    private void OnCoreMapInit(Entity<TerritoryCoreComponent> ent, ref MapInitEvent args)
    {
        if (IsActive(ent))
            Activate(ent);
    }

    private void OnControllerChanged(ref GridTerritoryControllerChangedEvent args)
    {
        if (args.OldSourceBanner is { } oldSource && _coreQuery.TryGetComponent(oldSource, out var oldCore))
            Deactivate((oldSource, oldCore));

        if (args.SourceBanner is { } source && _coreQuery.TryGetComponent(source, out var core) && IsActive(source))
            Activate((source, core));
    }

    private void Activate(Entity<TerritoryCoreComponent> ent)
    {
        var grid = _transformQuery.GetComponent(ent).GridUid;
        if (ent.Comp.ActiveGrid == grid)
            return;

        ent.Comp.ActiveGrid = grid;
        ent.Comp.NextSpawn = _timing.CurTime + ent.Comp.SpawnInterval;
        ent.Comp.NextCheck = ent.Comp.NextSpawn;
    }

    private void OnCoreShutdown(Entity<TerritoryCoreComponent> ent, ref ComponentShutdown args)
    {
        Deactivate(ent);
    }

    private void Deactivate(Entity<TerritoryCoreComponent> ent)
    {
        ent.Comp.ActiveGrid = null;
    }

    private void OnExamined(Entity<TerritoryCoreComponent> ent, ref ExaminedEvent args)
    {
        if (!IsActive(ent))
        {
            args.PushMarkup(Loc.GetString("exodus-hive-core-inactive"));
            return;
        }

        var remaining = ent.Comp.NextSpawn - _timing.CurTime;
        args.PushMarkup(remaining > TimeSpan.Zero
            ? Loc.GetString("exodus-hive-core-next")
            : Loc.GetString("exodus-hive-core-no-surface"));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<TerritoryCoreComponent>();
        while (query.MoveNext(out var uid, out var core))
        {
            if (core.ActiveGrid is not { } grid || now < core.NextCheck)
                continue;

            if (!IsActive(uid))
            {
                Deactivate((uid, core));
                continue;
            }

            if (core.SpawnInterval <= TimeSpan.Zero ||
                !TryComp<MapGridComponent>(grid, out var mapGrid) ||
                !TryFindSurface((uid, core), (grid, mapGrid), out var position))
            {
                core.NextCheck = now + (core.RetryInterval > TimeSpan.Zero ? core.RetryInterval : TimeSpan.FromSeconds(10));
                continue;
            }

            // Use the prototype's normal AI and ghost-role behavior, with no ongoing link to this core.
            Spawn(core.SpawnPrototype, position);

            // Start a fresh interval after success; pauses and blocked surfaces never produce a burst.
            core.NextSpawn = now + core.SpawnInterval;
            core.NextCheck = core.NextSpawn;
        }
    }

    private bool TryFindSurface(Entity<TerritoryCoreComponent> core, Entity<MapGridComponent> grid, out EntityCoordinates position)
    {
        position = default;
        var origin = _transform.WithEntityId(_transformQuery.GetComponent(core).Coordinates, grid.Owner).Position;
        var nearestDistance = float.PositiveInfinity;

        // Only scan this grid when a spawn is due. No per-tick scan of biomass or all entities.
        foreach (var uid in _map.GetLocalAnchoredEntities(grid.Owner, grid.Comp, grid.Comp.LocalAABB))
        {
            if (!_tag.HasTag(uid, core.Comp.SubstrateTag) || TerminatingOrDeleted(uid))
                continue;

            var coordinates = _transformQuery.GetComponent(uid).Coordinates;
            var local = _transform.WithEntityId(coordinates, grid.Owner);
            var distance = Vector2.DistanceSquared(origin, local.Position);
            if (distance >= nearestDistance || !IsFreeFloor(grid, local))
                continue;

            nearestDistance = distance;
            position = local;
        }

        return float.IsFinite(nearestDistance);
    }

    public bool IsFreeFloor(Entity<MapGridComponent> grid, EntityCoordinates coordinates)
    {
        var indices = _map.TileIndicesFor(grid.Owner, grid.Comp, coordinates);
        if (!_map.TryGetTileRef(grid.Owner, grid.Comp, indices, out var tile) || tile.Tile.IsEmpty ||
            !_anchorable.TileFree(grid, indices, (int)CollisionGroup.MobLayer, (int)CollisionGroup.MobMask))
        {
            return false;
        }

        var occupants = _map.GetAnchoredEntitiesEnumerator(grid.Owner, grid.Comp, indices);
        while (occupants.MoveNext(out var uid))
        {
            if (_coreQuery.HasComponent(uid.Value))
                return false;
        }

        return true;
    }
}
