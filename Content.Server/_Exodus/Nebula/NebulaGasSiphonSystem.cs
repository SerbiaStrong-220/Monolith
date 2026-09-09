using System.Collections.Generic;
using System.Numerics;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Server.Power.EntitySystems;
using Content.Server._Exodus.Nebula.Presence;
using Content.Shared._Exodus.Nebula;
using Content.Shared._Exodus.Nebula.Components;
using Content.Shared._Exodus.Nebula.Events;
using Content.Shared._NF.Atmos.Prototypes;
using Content.Shared.Atmos;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Physics;
using Content.Shared.Maps;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Collects gas into a pipe while the shuttle moves through dense nebula with clear space along both ends of the siphon.
/// </summary>
public sealed class NebulaGasSiphonSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly NodeContainerSystem _nodeContainer = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly PowerReceiverSystem _powerReceiver = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly IComponentFactory _componentFactory = default!;

    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<NebulaPresenceComponent> _presenceQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<NebulaGasSiphonGridComponent> _siphonGridQuery;
    private readonly PriorityQueue<EntityUid, TimeSpan> _siphonQueue = new();
    private readonly Dictionary<string, NebulaGasSiphonProfile?> _profiles = new();
    private readonly GasMixture _mergeBuffer = new();
    private readonly HashSet<EntityUid> _tileEntityBuffer = new();
    private readonly HashSet<EntityUid> _clearanceInvalidationBuffer = new();

    public override void Initialize()
    {
        base.Initialize();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _presenceQuery = GetEntityQuery<NebulaPresenceComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _siphonGridQuery = GetEntityQuery<NebulaGasSiphonGridComponent>();

        SubscribeLocalEvent<NebulaGasSiphonComponent, ComponentStartup>(OnSiphonStartup);
        SubscribeLocalEvent<NebulaGasSiphonComponent, MapInitEvent>(OnSiphonMapInit);
        SubscribeLocalEvent<NebulaGasSiphonComponent, ComponentRemove>(OnSiphonRemove);
        SubscribeLocalEvent<NebulaGasSiphonComponent, EntityUnpausedEvent>(OnSiphonUnpaused);
        SubscribeLocalEvent<NebulaGasSiphonComponent, AnchorStateChangedEvent>(OnSiphonAnchorChanged);
        SubscribeLocalEvent<NebulaGasSiphonComponent, MoveEvent>(OnSiphonMove);
        SubscribeLocalEvent<NebulaGasSiphonComponent, EntParentChangedMessage>(OnSiphonParentChanged);
        SubscribeLocalEvent<NebulaGasSiphonComponent, EntInsertedIntoContainerMessage>(OnFilterInserted);
        SubscribeLocalEvent<NebulaGasSiphonComponent, EntRemovedFromContainerMessage>(OnFilterRemoved);
        SubscribeLocalEvent<NebulaGasSiphonFilterComponent, ComponentStartup>(OnFilterStartup);
        SubscribeLocalEvent<NebulaGasSiphonComponent, ExaminedEvent>(OnSiphonExamined);
        SubscribeLocalEvent<ActiveNebulaGasSiphonComponent, ComponentShutdown>(OnActiveSiphonShutdown);
        SubscribeLocalEvent<NebulaGasSiphonFilterComponent, ExaminedEvent>(OnFilterExamined);
        SubscribeLocalEvent<NebulaPresenceChangedEvent>(OnPresenceChanged);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<CollisionChangeEvent>(OnCollisionChange);
        SubscribeLocalEvent<CollisionLayerChangeEvent>(OnCollisionLayerChange);
        SubscribeLocalEvent<PhysicsBodyTypeChangedEvent>(OnBodyTypeChange);
        SubscribeLocalEvent<FixturesComponent, ComponentStartup>(OnFixturesStartup);
        SubscribeLocalEvent<FixturesComponent, ComponentRemove>(OnFixturesRemove);
        SubscribeLocalEvent<FixturesComponent, MoveEvent>(OnFixturesMoved);
        SubscribeLocalEvent<AnchorStateChangedEvent>(OnAnchorStateChanged);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        var curTime = _timing.CurTime;

        while (_siphonQueue.TryPeek(out var uid, out var nextUpdate)
               && nextUpdate <= curTime)
        {
            _siphonQueue.Dequeue();

            if (TerminatingOrDeleted(uid)
                || MetaData(uid).EntityPaused
                || !TryComp<ActiveNebulaGasSiphonComponent>(uid, out var active)
                || !TryComp<NebulaGasSiphonComponent>(uid, out var siphon)
                || siphon.NextUpdate != nextUpdate)
            {
                continue;
            }

            ProcessSiphon(uid, siphon, active, curTime);
        }
    }

    private void ProcessSiphon(
        EntityUid uid,
        NebulaGasSiphonComponent siphon,
        ActiveNebulaGasSiphonComponent active,
        TimeSpan curTime)
    {
        if (siphon.UpdateInterval <= TimeSpan.Zero)
            return;

        if (!TryComp<TransformComponent>(uid, out var xform)
            || !xform.Anchored
            || xform.GridUid is not { } gridUid
            || !_presenceQuery.TryGetComponent(gridUid, out var presence))
        {
            RemCompDeferred<ActiveNebulaGasSiphonComponent>(uid);
            return;
        }

        if (siphon.NextUpdate == TimeSpan.Zero)
            siphon.NextUpdate = curTime;

        var nextUpdate = siphon.NextUpdate + siphon.UpdateInterval;
        if (nextUpdate < curTime)
            nextUpdate = curTime + siphon.UpdateInterval;

        if (!active.PhaseApplied)
        {
            nextUpdate += active.PhaseOffset;
            active.PhaseApplied = true;
        }

        siphon.NextUpdate = nextUpdate;
        _siphonQueue.Enqueue(uid, siphon.NextUpdate);

        if (presence.Density <= siphon.MinDensity)
            return;

        if (!_powerReceiver.IsPowered(uid))
            return;

        if (!_physicsQuery.TryGetComponent(gridUid, out var physics))
            return;

        var speed = physics.LinearVelocity.Length();
        if (speed < siphon.MinSpeed)
            return;

        if (!_gridQuery.TryGetComponent(gridUid, out var grid))
            return;

        if (!TryGetWorkingFilter(uid, out var filterUid, out var filter))
        {
            RemCompDeferred<ActiveNebulaGasSiphonComponent>(uid);
            return;
        }

        EnsurePipeEntity(uid, siphon);
        if (!TryGetPipeNetwork(siphon, out var net))
            return;

        if (!TryGetProfile(presence.Marker, out var profile))
        {
            RemCompDeferred<ActiveNebulaGasSiphonComponent>(uid);
            return;
        }

        if (!HasClearAxisCached(uid, xform, gridUid, grid, siphon, active))
            return;

        var densityMultiplier = Math.Clamp(presence.Density, 0f, 1f);
        var speedMultiplier = siphon.FullSpeed > 0f
            ? Math.Clamp(speed / siphon.FullSpeed, 0f, 1f)
            : 1f;
        var extractionRate = siphon.MolesPerSecond * densityMultiplier * speedMultiplier * profile.ExtractionMultiplier;
        if (extractionRate <= 0f || profile.Temperature <= 0f)
            return;

        var targetPressure = Math.Clamp(siphon.TargetPressure, 0f, Atmospherics.MaxOutputPressure);
        var toSpawn = (targetPressure - net.Air.Pressure) * net.Air.Volume /
                      (profile.Temperature * Atmospherics.R);
        toSpawn = MathF.Min(toSpawn, extractionRate * (float)siphon.UpdateInterval.TotalSeconds);

        if (siphon.MaxPipeMoles > 0f)
            toSpawn = MathF.Min(toSpawn, siphon.MaxPipeMoles - net.Air.TotalMoles);

        toSpawn = MathF.Min(toSpawn, filter.Remaining / filter.ConsumptionPerMole);

        if (toSpawn < Atmospherics.GasMinMoles)
            return;

        _mergeBuffer.CopyFrom(profile.Composition);
        _mergeBuffer.Multiply(toSpawn);
        _mergeBuffer.Temperature = profile.Temperature;
        _atmosphere.Merge(net.Air, _mergeBuffer);

        filter.Remaining = MathF.Max(0f, filter.Remaining - toSpawn * filter.ConsumptionPerMole);
        UpdateFilterAppearance(filterUid, filter);
        UpdateSiphonEmissionAppearance(uid, filter);

        if (filter.Remaining < Atmospherics.GasMinMoles)
        {
            RemoveSiphonFromGrid(gridUid, uid);
            RemCompDeferred<ActiveNebulaGasSiphonComponent>(uid);
        }
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        if (!_siphonGridQuery.TryGetComponent(args.Entity.Owner, out var gridSiphons))
            return;

        _clearanceInvalidationBuffer.Clear();
        foreach (var change in args.Changes)
        {
            var bucketKey = GetClearanceBucket(change.GridIndices);
            if (!gridSiphons.ClearanceBuckets.TryGetValue(bucketKey, out var bucket))
                continue;

            foreach (var uid in bucket)
                _clearanceInvalidationBuffer.Add(uid);
        }

        foreach (var uid in _clearanceInvalidationBuffer)
        {
            if (TryComp<ActiveNebulaGasSiphonComponent>(uid, out var active) && active.AxisCacheValid)
                active.AxisCacheValid = false;
        }
    }

    private void OnCollisionChange(ref CollisionChangeEvent args)
    {
        if (args.Body.BodyType != BodyType.Static
            || !TryComp<TransformComponent>(args.BodyUid, out var xform)
            || !HasSiphons(xform.GridUid))
        {
            return;
        }

        InvalidateGridEntityArea(xform.GridUid!.Value, args.BodyUid, xform.Coordinates, xform.LocalRotation);
    }

    private void OnCollisionLayerChange(ref CollisionLayerChangeEvent args)
    {
        if (args.Body.Comp.BodyType != BodyType.Static
            || !TryComp<TransformComponent>(args.Body.Owner, out var xform)
            || !HasSiphons(xform.GridUid))
        {
            return;
        }

        InvalidateGridEntityArea(xform.GridUid!.Value, args.Body.Owner, xform.Coordinates, xform.LocalRotation);
    }

    private void OnBodyTypeChange(ref PhysicsBodyTypeChangedEvent args)
    {
        if (args.New != BodyType.Static && args.Old != BodyType.Static
            || !TryComp<TransformComponent>(args.Entity, out var xform)
            || !HasSiphons(xform.GridUid))
        {
            return;
        }

        InvalidateGridEntityArea(xform.GridUid!.Value, args.Entity, xform.Coordinates, xform.LocalRotation);
    }

    private void OnFixturesStartup(Entity<FixturesComponent> ent, ref ComponentStartup args)
    {
        if (!IsStaticBody(ent.Owner)
            || !TryComp<TransformComponent>(ent.Owner, out var xform)
            || !HasSiphons(xform.GridUid))
        {
            return;
        }

        InvalidateGridEntityArea(xform.GridUid!.Value, ent.Owner, xform.Coordinates, xform.LocalRotation);
    }

    private void OnFixturesRemove(Entity<FixturesComponent> ent, ref ComponentRemove args)
    {
        if (IsStaticBody(ent.Owner) && TryComp<TransformComponent>(ent.Owner, out var xform))
            InvalidateGrid(xform.GridUid);
    }

    private void OnFixturesMoved(Entity<FixturesComponent> ent, ref MoveEvent args)
    {
        if (_gridQuery.HasComp(ent.Owner) || !IsStaticBody(ent.Owner))
            return;

        InvalidateMovedFixtureArea(ent.Owner, args.OldPosition, args.OldRotation);
        InvalidateMovedFixtureArea(ent.Owner, args.NewPosition, args.NewRotation);
    }

    private void InvalidateMovedFixtureArea(
        EntityUid entityUid,
        EntityCoordinates coordinates,
        Angle rotation)
    {
        if (ResolveGridUid(coordinates.EntityId) is not { } gridUid || !HasSiphons(gridUid))
            return;

        InvalidateGridEntityArea(gridUid, entityUid, coordinates, rotation);
    }

    private void OnAnchorStateChanged(ref AnchorStateChangedEvent args)
    {
        if (args.Transform.GridUid is not { } gridUid)
            return;

        if (_gridQuery.HasComp(args.Entity))
        {
            InvalidateGrid(args.Entity);
            return;
        }

        if (!HasComp<FixturesComponent>(args.Entity)
            || !IsStaticBody(args.Entity)
            || !HasSiphons(gridUid))
            return;

        InvalidateGridEntityArea(
            gridUid,
            args.Entity,
            args.Transform.Coordinates,
            args.Transform.LocalRotation);
    }

    private bool IsStaticBody(EntityUid uid)
    {
        return _physicsQuery.TryGetComponent(uid, out var physics) && physics.BodyType == BodyType.Static;
    }

    private bool HasSiphons(EntityUid? gridUid)
    {
        return gridUid is { } grid && _siphonGridQuery.HasComp(grid);
    }

    private void InvalidateGrid(EntityUid? gridUid)
    {
        if (gridUid is not { } grid
            || !_siphonGridQuery.TryGetComponent(grid, out var gridSiphons))
        {
            return;
        }

        _clearanceInvalidationBuffer.Clear();
        foreach (var bucket in gridSiphons.ClearanceBuckets.Values)
        {
            foreach (var uid in bucket)
                _clearanceInvalidationBuffer.Add(uid);
        }

        foreach (var uid in _clearanceInvalidationBuffer)
        {
            if (TryComp<ActiveNebulaGasSiphonComponent>(uid, out var active))
                active.AxisCacheValid = false;
        }
    }

    private void InvalidateGridEntityArea(
        EntityUid gridUid,
        EntityUid entityUid,
        EntityCoordinates coordinates,
        Angle rotation)
    {
        var localAabb = _lookup.GetAABBNoContainer(entityUid, coordinates.Position, rotation);
        if (coordinates.EntityId == gridUid)
        {
            InvalidateGridArea(gridUid, localAabb);
            return;
        }

        if (!TryComp<TransformComponent>(coordinates.EntityId, out var parentXform))
        {
            InvalidateGrid(gridUid);
            return;
        }

        var worldAabb = _transform.GetWorldMatrix(parentXform).TransformBox(localAabb);
        var gridLocalAabb = _transform.GetInvWorldMatrix(gridUid).TransformBox(worldAabb);
        InvalidateGridArea(gridUid, gridLocalAabb);
    }

    private void InvalidateGridArea(EntityUid? gridUid, Box2 localAabb)
    {
        if (gridUid is not { } grid
            || !_siphonGridQuery.TryGetComponent(grid, out var gridSiphons)
            || !_gridQuery.TryGetComponent(grid, out var gridComponent))
        {
            return;
        }

        var minTile = new Vector2i(
            (int)MathF.Floor(localAabb.Left / gridComponent.TileSize),
            (int)MathF.Floor(localAabb.Bottom / gridComponent.TileSize));
        var maxTile = new Vector2i(
            (int)MathF.Floor(localAabb.Right / gridComponent.TileSize),
            (int)MathF.Floor(localAabb.Top / gridComponent.TileSize));
        var minBucket = GetClearanceBucket(minTile);
        var maxBucket = GetClearanceBucket(maxTile);

        _clearanceInvalidationBuffer.Clear();
        for (var x = minBucket.X; x <= maxBucket.X; x++)
        {
            for (var y = minBucket.Y; y <= maxBucket.Y; y++)
            {
                if (!gridSiphons.ClearanceBuckets.TryGetValue(new Vector2i(x, y), out var bucket))
                    continue;

                foreach (var uid in bucket)
                    _clearanceInvalidationBuffer.Add(uid);
            }
        }

        foreach (var uid in _clearanceInvalidationBuffer)
        {
            if (TryComp<ActiveNebulaGasSiphonComponent>(uid, out var active) && active.AxisCacheValid)
                active.AxisCacheValid = false;
        }
    }

    private static Vector2i GetClearanceBucket(Vector2i tile)
    {
        var bucketSize = (float)NebulaGasSiphonGridComponent.ClearanceBucketSize;
        return new Vector2i(
            (int)MathF.Floor(tile.X / bucketSize),
            (int)MathF.Floor(tile.Y / bucketSize));
    }

    private void InvalidateSiphonAxisCache(EntityUid uid)
    {
        if (!TryComp<ActiveNebulaGasSiphonComponent>(uid, out var active))
            return;

        RemoveClearanceIndex(uid, active);
        active.AxisCacheValid = false;
    }

    private void AddClearanceIndex(
        EntityUid uid,
        EntityUid gridUid,
        ActiveNebulaGasSiphonComponent active)
    {
        if (!_siphonGridQuery.TryGetComponent(gridUid, out var gridSiphons))
            return;

        var minBucket = GetClearanceBucket(active.ClearanceMin);
        var maxBucket = GetClearanceBucket(active.ClearanceMax);
        active.ClearanceGrid = gridUid;

        for (var x = minBucket.X; x <= maxBucket.X; x++)
        {
            for (var y = minBucket.Y; y <= maxBucket.Y; y++)
            {
                var bucketKey = new Vector2i(x, y);
                if (!gridSiphons.ClearanceBuckets.TryGetValue(bucketKey, out var bucket))
                {
                    bucket = new HashSet<EntityUid>();
                    gridSiphons.ClearanceBuckets.Add(bucketKey, bucket);
                }

                bucket.Add(uid);
                active.ClearanceBucketKeys.Add(bucketKey);
            }
        }
    }

    private void RemoveClearanceIndex(EntityUid uid, ActiveNebulaGasSiphonComponent active)
    {
        if (active.ClearanceGrid is not { } grid
            || !_siphonGridQuery.TryGetComponent(grid, out var gridSiphons))
        {
            active.ClearanceBucketKeys.Clear();
            active.ClearanceGrid = null;
            return;
        }

        foreach (var bucketKey in active.ClearanceBucketKeys)
        {
            if (!gridSiphons.ClearanceBuckets.TryGetValue(bucketKey, out var bucket))
                continue;

            bucket.Remove(uid);
            if (bucket.Count == 0)
                gridSiphons.ClearanceBuckets.Remove(bucketKey);
        }

        active.ClearanceBucketKeys.Clear();
        active.ClearanceGrid = null;
    }

    private void OnActiveSiphonShutdown(Entity<ActiveNebulaGasSiphonComponent> ent, ref ComponentShutdown args)
    {
        RemoveClearanceIndex(ent.Owner, ent.Comp);
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        _siphonQueue.Clear();
    }

    private void OnSiphonStartup(Entity<NebulaGasSiphonComponent> ent, ref ComponentStartup args)
    {
        RefreshSiphonAppearance(ent.Owner);

        if (!TryComp<TransformComponent>(ent.Owner, out var xform)
            || !IsMapInitialized(xform))
        {
            return;
        }

        InitializeSiphon(ent);
    }

    private void OnSiphonMapInit(Entity<NebulaGasSiphonComponent> ent, ref MapInitEvent args)
    {
        InitializeSiphon(ent);
    }

    private void OnSiphonAnchorChanged(Entity<NebulaGasSiphonComponent> ent, ref AnchorStateChangedEvent args)
    {
        InvalidateSiphonAxisCache(ent.Owner);

        if (args.Anchored)
        {
            if (!IsMapInitialized(args.Transform))
                return;

            EnsurePipeEntity(ent.Owner, ent.Comp);
            UpdateSiphonIndex(ent.Owner);
            UpdateSiphonActivity(ent.Owner);
            return;
        }

        DeletePipeEntity(ent.Comp);
        RemoveSiphonFromGrid(args.Transform.GridUid, ent.Owner);
        RemCompDeferred<ActiveNebulaGasSiphonComponent>(ent.Owner);
    }

    private void OnSiphonMove(Entity<NebulaGasSiphonComponent> ent, ref MoveEvent args)
    {
        InvalidateSiphonAxisCache(ent.Owner);
    }

    private void OnSiphonParentChanged(Entity<NebulaGasSiphonComponent> ent, ref EntParentChangedMessage args)
    {
        RemoveSiphonFromGrid(ResolveGridUid(args.OldParent), ent.Owner);
        InvalidateSiphonAxisCache(ent.Owner);
        UpdateSiphonIndex(ent.Owner);
        UpdateSiphonActivity(ent.Owner);
    }

    private void OnSiphonUnpaused(Entity<NebulaGasSiphonComponent> ent, ref EntityUnpausedEvent args)
    {
        ent.Comp.NextUpdate += args.PausedTime;
        UpdateSiphonActivity(ent.Owner);
    }

    private void OnPresenceChanged(ref NebulaPresenceChangedEvent args)
    {
        if (!_siphonGridQuery.TryGetComponent(args.Entity, out var gridSiphons))
            return;

        var active = args.NewMarker.Id is not null;
        foreach (var uid in gridSiphons.Siphons)
        {
            if (!TryComp<NebulaGasSiphonComponent>(uid, out var siphon)
                || !TryComp<TransformComponent>(uid, out var xform))
            {
                continue;
            }

            SetSiphonActivity(uid, siphon, xform, active);
        }
    }

    private void UpdateSiphonIndex(EntityUid uid)
    {
        if (!TryComp<NebulaGasSiphonComponent>(uid, out _)
            || !TryComp<TransformComponent>(uid, out var xform))
        {
            return;
        }

        if (!xform.Anchored
            || xform.GridUid is not { } gridUid
            || !_gridQuery.HasComp(gridUid)
            || !TryGetWorkingFilter(uid, out _, out _))
        {
            RemoveSiphonFromGrid(xform.GridUid, uid);
            return;
        }

        EnsureComp<NebulaGasSiphonGridComponent>(gridUid).Siphons.Add(uid);
    }

    private void RemoveSiphonFromGrid(EntityUid? gridUid, EntityUid siphonUid)
    {
        if (TryComp<ActiveNebulaGasSiphonComponent>(siphonUid, out var active))
            RemoveClearanceIndex(siphonUid, active);

        if (gridUid is not { } grid
            || !_siphonGridQuery.TryGetComponent(grid, out var gridSiphons)
            || !gridSiphons.Siphons.Remove(siphonUid))
        {
            return;
        }

        if (gridSiphons.Siphons.Count == 0 && gridSiphons.ClearanceBuckets.Count == 0)
            RemCompDeferred<NebulaGasSiphonGridComponent>(grid);
    }

    private EntityUid? ResolveGridUid(EntityUid? entity)
    {
        if (entity is not { } uid)
            return null;

        if (_gridQuery.HasComp(uid))
            return uid;

        return TryComp<TransformComponent>(uid, out var xform)
            ? xform.GridUid
            : null;
    }

    private void UpdateSiphonActivity(EntityUid uid)
    {
        if (!TryComp<NebulaGasSiphonComponent>(uid, out var siphon)
            || !TryComp<TransformComponent>(uid, out var xform)
            || xform.GridUid is not { } gridUid
            || !xform.Anchored)
        {
            RemCompDeferred<ActiveNebulaGasSiphonComponent>(uid);
            return;
        }

        SetSiphonActivity(uid, siphon, xform, _presenceQuery.HasComp(gridUid));
    }

    private void SetSiphonActivity(
        EntityUid uid,
        NebulaGasSiphonComponent siphon,
        TransformComponent xform,
        bool active)
    {
        if (!active
            || !xform.Anchored
            || xform.GridUid is null
            || !TryGetWorkingFilter(uid, out _, out _))
        {
            RemCompDeferred<ActiveNebulaGasSiphonComponent>(uid);
            return;
        }

        var activeSiphon = EnsureComp<ActiveNebulaGasSiphonComponent>(uid);
        if (!activeSiphon.PhaseInitialized)
            siphon.NextUpdate = _timing.CurTime;

        EnsureSiphonPhase(uid, siphon, activeSiphon);
        ScheduleSiphon(uid, siphon);
    }

    private void OnFilterInserted(Entity<NebulaGasSiphonComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID != NebulaGasSiphonComponent.FilterSlotId)
            return;

        UpdateSiphonAppearance(ent.Owner, true);
        if (TryComp<NebulaGasSiphonFilterComponent>(args.Entity, out var filter))
            UpdateSiphonEmissionAppearance(ent.Owner, filter);

        UpdateSiphonIndex(ent.Owner);
        UpdateSiphonActivity(ent.Owner);
    }

    private bool TryGetWorkingFilter(
        EntityUid uid,
        out EntityUid filterUid,
        out NebulaGasSiphonFilterComponent filter)
    {
        filterUid = default;
        filter = default!;

        if (!TryGetFilterItem(uid, out var itemUid)
            || !TryComp<NebulaGasSiphonFilterComponent>(itemUid, out var filterComp)
            || filterComp.Remaining < Atmospherics.GasMinMoles
            || filterComp.ConsumptionPerMole <= 0f)
        {
            return false;
        }

        filterUid = itemUid;
        filter = filterComp;
        return true;
    }

    private void ScheduleSiphon(EntityUid uid, NebulaGasSiphonComponent siphon)
    {
        if (!HasComp<ActiveNebulaGasSiphonComponent>(uid)
            || siphon.UpdateInterval <= TimeSpan.Zero)
        {
            return;
        }

        var curTime = _timing.CurTime;
        if (siphon.NextUpdate < curTime)
            siphon.NextUpdate = curTime;

        _siphonQueue.Enqueue(uid, siphon.NextUpdate);
    }

    private void OnFilterRemoved(Entity<NebulaGasSiphonComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != NebulaGasSiphonComponent.FilterSlotId)
            return;

        UpdateSiphonAppearance(ent.Owner, false);
        if (TryComp<TransformComponent>(ent.Owner, out var xform))
            RemoveSiphonFromGrid(xform.GridUid, ent.Owner);

        RemCompDeferred<ActiveNebulaGasSiphonComponent>(ent.Owner);
    }

    private void OnSiphonRemove(Entity<NebulaGasSiphonComponent> ent, ref ComponentRemove args)
    {
        DeletePipeEntity(ent.Comp);
        if (TryComp<TransformComponent>(ent.Owner, out var xform))
            RemoveSiphonFromGrid(xform.GridUid, ent.Owner);

        RemCompDeferred<ActiveNebulaGasSiphonComponent>(ent.Owner);
    }

    private void EnsurePipeEntity(EntityUid uid, NebulaGasSiphonComponent siphon)
    {
        if (siphon.PipeEntity is { } pipeEntity && !TerminatingOrDeleted(pipeEntity))
            return;

        if (!TryComp<TransformComponent>(uid, out var xform) || !xform.Anchored)
            return;

        siphon.PipeEntity = SpawnAttachedTo(
            siphon.PipePrototype,
            new EntityCoordinates(uid, siphon.PipePosition),
            rotation: Angle.Zero);
    }

    private bool TryGetPipeNetwork(NebulaGasSiphonComponent siphon, out PipeNet net)
    {
        net = default!;
        if (siphon.PipeEntity is not { } pipeEntity
            || TerminatingOrDeleted(pipeEntity)
            || !_nodeContainer.TryGetNode(pipeEntity, siphon.PipeNodeName, out PipeNode? pipe)
            || pipe.NodeGroup is not PipeNet { NodeCount: > 1 } pipeNet)
        {
            return false;
        }

        net = pipeNet;
        return true;
    }

    private void OnFilterStartup(Entity<NebulaGasSiphonFilterComponent> ent, ref ComponentStartup args)
    {
        if (ent.Comp.Remaining < 0f)
            ent.Comp.Remaining = MathF.Max(0f, ent.Comp.Capacity);

        UpdateFilterAppearance(ent.Owner, ent.Comp);

        if (Transform(ent.Owner).ParentUid is { } parent
            && HasComp<NebulaGasSiphonComponent>(parent)
            && TryGetFilterItem(parent, out var filterUid)
            && filterUid == ent.Owner)
        {
            UpdateSiphonAppearance(parent, true);
            UpdateSiphonEmissionAppearance(parent, ent.Comp);
            UpdateSiphonIndex(parent);
            UpdateSiphonActivity(parent);
        }
    }

    private void InitializeSiphon(Entity<NebulaGasSiphonComponent> ent)
    {
        EnsurePipeEntity(ent.Owner, ent.Comp);
        UpdateSiphonIndex(ent.Owner);
        UpdateSiphonActivity(ent.Owner);
    }

    private void RefreshSiphonAppearance(EntityUid uid)
    {
        if (TryGetFilterItem(uid, out var filterUid)
            && TryComp<NebulaGasSiphonFilterComponent>(filterUid, out var filter))
        {
            UpdateSiphonAppearance(uid, true);
            UpdateSiphonEmissionAppearance(uid, filter);
            return;
        }

        UpdateSiphonAppearance(uid, false);
    }

    private bool TryGetFilterItem(EntityUid uid, out EntityUid itemUid)
    {
        itemUid = default;

        if (!_itemSlots.TryGetSlot(uid, NebulaGasSiphonComponent.FilterSlotId, out var slot)
            || slot.Item is not { } filterUid)
        {
            return false;
        }

        itemUid = filterUid;
        return true;
    }

    private void DeletePipeEntity(NebulaGasSiphonComponent siphon)
    {
        if (siphon.PipeEntity is not { } pipeEntity)
            return;

        siphon.PipeEntity = null;
        if (!TerminatingOrDeleted(pipeEntity))
            Del(pipeEntity);
    }

    private bool IsMapInitialized(TransformComponent xform)
    {
        return xform.MapID != MapId.Nullspace && _map.IsInitialized(xform.MapID);
    }

    private void OnSiphonExamined(Entity<NebulaGasSiphonComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        using (args.PushGroup(nameof(NebulaGasSiphonComponent)))
        {
            if (!TryComp<TransformComponent>(ent.Owner, out var xform)
                || !xform.Anchored
                || xform.GridUid is not { } gridUid
                || !_gridQuery.TryGetComponent(gridUid, out var grid))
            {
                args.PushMarkup(Loc.GetString("nebula-gas-siphon-examine-unanchored"));
                return;
            }

            var hasWorkingFilter = TryGetWorkingFilter(ent.Owner, out _, out _);
            _presenceQuery.TryGetComponent(gridUid, out var presence);
            var hasSufficientDensity = presence is not null && presence.Density > ent.Comp.MinDensity;
            var hasProfile = presence is not null && TryGetProfile(presence.Marker, out _);
            var powered = _powerReceiver.IsPowered(ent.Owner);
            _physicsQuery.TryGetComponent(gridUid, out var physics);
            var speed = physics?.LinearVelocity.Length() ?? 0f;
            var hasSufficientSpeed = speed >= ent.Comp.MinSpeed;
            var pipeConnected = TryGetPipeNetwork(ent.Comp, out var net);
            var targetPressure = Math.Clamp(ent.Comp.TargetPressure, 0f, Atmospherics.MaxOutputPressure);
            var outputHasCapacity = pipeConnected
                && net.Air.Pressure < targetPressure
                && (ent.Comp.MaxPipeMoles <= 0f || net.Air.TotalMoles < ent.Comp.MaxPipeMoles);
            var axisClear = ComputeHasClearAxis(xform, gridUid, grid, ent.Comp);
            var operating = hasWorkingFilter
                && hasSufficientDensity
                && hasProfile
                && powered
                && hasSufficientSpeed
                && pipeConnected
                && outputHasCapacity
                && axisClear;

            args.PushMarkup(Loc.GetString(operating
                ? "nebula-gas-siphon-examine-operating"
                : "nebula-gas-siphon-examine-waiting"));

            args.PushMarkup(Loc.GetString(axisClear
                ? "nebula-gas-siphon-examine-axis-clear"
                : "nebula-gas-siphon-examine-axis-blocked",
                ("tiles", ent.Comp.Range)));

            if (!hasSufficientSpeed)
                args.PushMarkup(Loc.GetString("nebula-gas-siphon-examine-speed-low",
                    ("speed", MathF.Round(speed, 1)),
                    ("required", ent.Comp.MinSpeed)));
        }
    }

    private void OnFilterExamined(Entity<NebulaGasSiphonFilterComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        var percent = GetRemainingStage(ent.Comp) * 100 / NebulaGasSiphonFilterComponent.RemainingStageCount;
        args.PushMarkup(Loc.GetString("nebula-gas-siphon-filter-examine", ("percent", percent)));
    }

    private void UpdateFilterAppearance(EntityUid uid, NebulaGasSiphonFilterComponent filter)
    {
        var state = filter.Remaining >= Atmospherics.GasMinMoles
            ? NebulaGasSiphonFilterState.Intact
            : NebulaGasSiphonFilterState.Depleted;
        _appearance.SetData(uid, NebulaGasSiphonFilterVisuals.State, state);

        var remainingStage = GetRemainingStage(filter);
        if (filter.RemainingStage == remainingStage)
            return;

        filter.RemainingStage = remainingStage;
        Dirty(uid, filter);
    }

    private static byte GetRemainingStage(NebulaGasSiphonFilterComponent filter)
    {
        if (filter.Capacity <= 0f || filter.Remaining < Atmospherics.GasMinMoles)
            return 0;

        var fillRatio = Math.Clamp(filter.Remaining / filter.Capacity, 0f, 1f);
        var stage = (int)MathF.Floor(fillRatio * NebulaGasSiphonFilterComponent.RemainingStageCount);
        return (byte)Math.Clamp(stage, 0, NebulaGasSiphonFilterComponent.RemainingStageCount);
    }

    private void EnsureSiphonPhase(
        EntityUid uid,
        NebulaGasSiphonComponent siphon,
        ActiveNebulaGasSiphonComponent active)
    {
        if (active.PhaseInitialized && active.PhaseInterval == siphon.UpdateInterval)
            return;

        active.PhaseOffset = GetPhaseOffset(uid, siphon.UpdateInterval);
        active.PhaseInterval = siphon.UpdateInterval;
        active.PhaseInitialized = true;
        active.PhaseApplied = false;
    }

    private static TimeSpan GetPhaseOffset(EntityUid uid, TimeSpan interval)
    {
        var intervalTicks = interval.Ticks;
        if (intervalTicks <= 1)
            return TimeSpan.Zero;

        var hash = unchecked((uint)uid.Id);
        hash ^= hash >> 16;
        hash = unchecked(hash * 0x7FEB352Du);
        hash ^= hash >> 15;
        hash = unchecked(hash * 0x846CA68Bu);
        hash ^= hash >> 16;

        return TimeSpan.FromTicks((long)(hash % (ulong)intervalTicks));
    }

    private void UpdateSiphonAppearance(EntityUid uid, bool filterInstalled)
    {
        _appearance.SetData(uid, NebulaGasSiphonVisuals.FilterState,
            filterInstalled ? NebulaGasSiphonState.Full : NebulaGasSiphonState.Empty);

        if (!filterInstalled)
            _appearance.SetData(uid, NebulaGasSiphonVisuals.EmissionState, NebulaGasSiphonEmissionState.Empty);
    }

    private void UpdateSiphonEmissionAppearance(EntityUid uid, NebulaGasSiphonFilterComponent filter)
    {
        _appearance.SetData(uid, NebulaGasSiphonVisuals.EmissionState, GetEmissionState(filter));
    }

    private static NebulaGasSiphonEmissionState GetEmissionState(NebulaGasSiphonFilterComponent filter)
    {
        if (filter.Capacity <= 0f || filter.Remaining < Atmospherics.GasMinMoles)
            return NebulaGasSiphonEmissionState.Empty;

        var fillRatio = Math.Clamp(filter.Remaining / filter.Capacity, 0f, 1f);
        var state = (int)MathF.Floor((1f - fillRatio) * 4f);
        return (NebulaGasSiphonEmissionState)Math.Clamp(state, 0, 4);
    }

    private bool TryGetProfile(EntProtoId marker, out NebulaGasSiphonProfile profile)
    {
        profile = default!;

        if (marker.Id is not { } markerId)
            return false;

        if (_profiles.TryGetValue(markerId, out var cached))
        {
            if (cached is null)
                return false;

            profile = cached;
            return true;
        }

        if (!NebulaQueryHelper.TryGetMarkerComponent(_prototype, _componentFactory, marker,
                out NebulaGasSiphonProfileComponent config)
            || !_prototype.TryIndex<GasDepositPrototype>(config.Composition, out var compositionPrototype))
        {
            _profiles[markerId] = null;
            return false;
        }

        var composition = new GasMixture();
        var totalMoles = 0f;
        for (var i = 0; i < compositionPrototype.Gases.Length && i < Atmospherics.TotalNumberOfGases; i++)
        {
            var gasRange = compositionPrototype.Gases[i];
            var moles = (gasRange.X + gasRange.Y) * 0.5f;
            if (moles <= 0f)
                continue;

            composition.SetMoles(i, moles);
            totalMoles += moles;
        }

        if (totalMoles < Atmospherics.GasMinMoles)
        {
            _profiles[markerId] = null;
            return false;
        }

        composition.Multiply(1f / totalMoles);
        profile = new NebulaGasSiphonProfile(
            composition,
            MathF.Max(config.Temperature, 1f),
            MathF.Max(config.ExtractionMultiplier, 0f));
        _profiles[markerId] = profile;
        return true;
    }

    private bool HasClearAxisCached(
        EntityUid uid,
        TransformComponent xform,
        EntityUid gridUid,
        MapGridComponent grid,
        NebulaGasSiphonComponent siphon,
        ActiveNebulaGasSiphonComponent active)
    {
        if (!active.AxisCacheValid)
        {
            RemoveClearanceIndex(uid, active);
            UpdateClearanceBounds(xform, gridUid, grid, siphon, active);
            active.AxisClear = ComputeHasClearAxis(xform, gridUid, grid, siphon);
            AddClearanceIndex(uid, gridUid, active);
            active.AxisCacheValid = true;
        }

        return active.AxisClear;
    }

    private void UpdateClearanceBounds(
        TransformComponent xform,
        EntityUid gridUid,
        MapGridComponent grid,
        NebulaGasSiphonComponent siphon,
        ActiveNebulaGasSiphonComponent active)
    {
        var center = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var range = Math.Max(0, siphon.Range);
        if (range == 0)
        {
            active.ClearanceMin = center;
            active.ClearanceMax = center;
            return;
        }

        var dir = (xform.LocalRotation + siphon.SpaceAxisRotation).GetCardinalDir().ToIntVec();
        var firstFreeTile = GetFirstFreeTile(siphon.FootprintLength);
        var lastFreeTile = firstFreeTile + range - 1;
        var forward = center + dir * lastFreeTile;
        var backward = center - dir * lastFreeTile;
        active.ClearanceMin = new Vector2i(
            Math.Min(forward.X, backward.X),
            Math.Min(forward.Y, backward.Y));
        active.ClearanceMax = new Vector2i(
            Math.Max(forward.X, backward.X),
            Math.Max(forward.Y, backward.Y));
    }

    private bool ComputeHasClearAxis(
        TransformComponent xform,
        EntityUid gridUid,
        MapGridComponent grid,
        NebulaGasSiphonComponent siphon)
    {
        var tile = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        var dir = (xform.LocalRotation + siphon.SpaceAxisRotation).GetCardinalDir();
        var forward = dir.ToIntVec();
        var backward = -forward;
        var firstFreeTile = GetFirstFreeTile(siphon.FootprintLength);

        for (var i = firstFreeTile; i < firstFreeTile + siphon.Range; i++)
        {
            if (!IsClearTile(gridUid, grid, tile + forward * i))
                return false;

            if (!IsClearTile(gridUid, grid, tile + backward * i))
                return false;
        }

        return true;
    }

    internal static int GetFirstFreeTile(int footprintLength)
    {
        return Math.Max(0, footprintLength) / 2 + 1;
    }

    private bool IsClearTile(EntityUid gridUid, MapGridComponent grid, Vector2i indices)
    {
        if (!_map.TryGetTileRef(gridUid, grid, indices, out var tileRef))
            return true;

        return (_turf.IsSpace(tileRef) || tileRef.Tile.IsEmpty)
            && !IsTileBlockedByStatic(gridUid, grid, indices);
    }

    private bool IsTileBlockedByStatic(EntityUid gridUid, MapGridComponent grid, Vector2i indices)
    {
        var tileSize = grid.TileSize;
        var tileCenter = (indices + new Vector2(0.5f, 0.5f)) * tileSize;
        var tileBounds = Box2.UnitCentered.Scale(0.95f * tileSize).Translated(tileCenter);

        // Dynamic movers are intentionally excluded: their movement must not invalidate or rebuild this cache.
        _tileEntityBuffer.Clear();
        _lookup.GetLocalEntitiesIntersecting(gridUid, tileBounds, _tileEntityBuffer, LookupFlags.Static);
        foreach (var entity in _tileEntityBuffer)
        {
            if (!_fixturesQuery.TryGetComponent(entity, out var fixtures))
                continue;

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (fixture.Hard && (fixture.CollisionLayer & (int)CollisionGroup.MobMask) != 0)
                    return true;
            }
        }

        return false;
    }

    private sealed record NebulaGasSiphonProfile(GasMixture Composition, float Temperature, float ExtractionMultiplier);
}
