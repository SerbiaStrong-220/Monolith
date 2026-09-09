using System.Numerics;
using Content.Shared._Exodus.StarSystem;
using Content.Shared._NF.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server._Exodus.Worldgen;

/// <summary>
/// Round-start placement shared by ordinary and nebula POIs. No update loop or runtime following.
/// Deferred requests are processed before nebulas and again after their roots have spawned.
/// </summary>
public sealed class RelativePoiSpawnSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly MapLoaderSystem _loader = default!;

    public void Begin(MapId map)
    {
        if (!_maps.TryGetMap(map, out var mapUid))
            return;

        var state = EnsureComp<RelativePoiGenerationComponent>(mapUid.Value);
        state.Rules.Clear();
        state.InvalidTargets.Clear();
        state.Pending.Clear();
        foreach (var rule in _prototypes.EnumeratePrototypes<RelativePoiPlacementPrototype>())
        {
            if ((rule.Poi == null) == (rule.NebulaPoi == null))
            {
                // A malformed two-target rule must not silently enable ordinary placement.
                if (rule.Poi is { } invalidPoi)
                {
                    var key = new PoiSpawnKey(false, invalidPoi.Id);
                    state.Rules.TryAdd(key, rule);
                    state.InvalidTargets.Add(key);
                }
                if (rule.NebulaPoi is { } invalidNebulaPoi)
                {
                    var key = new PoiSpawnKey(true, invalidNebulaPoi.Id);
                    state.Rules.TryAdd(key, rule);
                    state.InvalidTargets.Add(key);
                }
                Log.Error($"Relative POI rule {rule.ID}: specify exactly one of poi / nebulaPoi.");
                continue;
            }

            var target = Target(rule);
            if (!state.Rules.TryAdd(target, rule))
            {
                state.InvalidTargets.Add(target);
                Log.Error($"Multiple relative POI rules target {target}.");
            }

            var anchorCount = (rule.AnchorPoi != null ? 1 : 0) +
                              (rule.AnchorNebulaPoi != null ? 1 : 0) +
                              (rule.AnchorPlanet != null ? 1 : 0);
            if (anchorCount != 1 ||
                !float.IsFinite(rule.MinDistance) || !float.IsFinite(rule.MaxDistance) ||
                rule.MinDistance < 0 || rule.MaxDistance < rule.MinDistance ||
                rule.Poi is { } poi && !_prototypes.HasIndex(poi) ||
                rule.NebulaPoi is { } nebula && !_prototypes.HasIndex(nebula) ||
                rule.AnchorPoi is { } anchor && !_prototypes.HasIndex(anchor) ||
                rule.AnchorNebulaPoi is { } anchorNebula && !_prototypes.HasIndex(anchorNebula) ||
                rule.AnchorPlanet is { } anchorPlanet && !_prototypes.HasIndex(anchorPlanet))
            {
                state.InvalidTargets.Add(target);
                Log.Error($"Relative POI rule {rule.ID}: invalid anchor, prototype reference or distance range.");
            }
        }

        // Check every dependency path before spawning anything, including cross-spawner cycles.
        foreach (var (target, rule) in state.Rules)
        {
            var visited = new HashSet<PoiSpawnKey>();
            var current = target;
            while (state.Rules.TryGetValue(current, out var dependency))
            {
                if (!visited.Add(current) || state.InvalidTargets.Contains(current))
                {
                    state.InvalidTargets.Add(target);
                    Log.Error($"Relative POI rule {rule.ID}: cyclic or invalid dependency chain.");
                    break;
                }

                // Planets are generated independently and terminate POI dependency chains.
                if (dependency.AnchorPlanet != null)
                    break;

                current = Anchor(dependency);
            }
        }
    }

    /// <summary>
    /// True means this target is handled by relative placement, including invalid rules.
    /// The caller must not fall back to ordinary placement or select an additional copy.
    /// </summary>
    public bool QueueIfRelative(MapId map, PoiSpawnKey target, List<EntityUid>? output = null,
        string? overrideName = null, int? depotIndex = null)
    {
        if (!_maps.TryGetMap(map, out var mapUid) ||
            !TryComp<RelativePoiGenerationComponent>(mapUid, out var state) ||
            !state.Rules.TryGetValue(target, out var rule))
            return false;

        if (!state.InvalidTargets.Contains(target))
            state.Pending.Add(new RelativePoiSpawnRequest(rule.ID, output, overrideName, depotIndex));
        return true;
    }

    public void ProcessPending(MapId map, bool final = false)
    {
        if (!_maps.TryGetMap(map, out var mapUid) ||
            !TryComp<RelativePoiGenerationComponent>(mapUid, out var state))
            return;

        bool progressed;
        do
        {
            progressed = false;
            for (var i = 0; i < state.Pending.Count;)
            {
                var request = state.Pending[i];
                if (!_prototypes.TryIndex(request.Rule, out var rule))
                {
                    state.Pending.RemoveAt(i);
                    continue;
                }

                if (FindAnchors(map, rule).Count == 0)
                {
                    i++;
                    continue;
                }

                state.Pending.RemoveAt(i);
                var ev = new RelativePoiSpawnEvent(map, rule, request);
                RaiseLocalEvent(ref ev);
                progressed = true;
            }
        } while (progressed && state.Pending.Count > 0);

        if (!final)
            return;

        foreach (var request in state.Pending)
            Log.Error($"Relative POI {request.Rule}: anchor did not spawn on map {map}; dependent POI skipped.");
        state.Pending.Clear();
        RemComp<RelativePoiGenerationComponent>(mapUid.Value);
    }

    public void Register(EntityUid grid, PoiSpawnKey key, float clearance, int? nebulaCandidate = null)
    {
        var identity = EnsureComp<PoiSpawnIdentityComponent>(grid);
        identity.Key = key;
        identity.Clearance = MathF.Max(0, clearance);
        identity.NebulaCandidate = nebulaCandidate;
    }

    public bool HasNebulaCopy(MapId map, string id, int candidate)
    {
        // Round-start maps and their grids can still be paused.
        var query = AllEntityQuery<PoiSpawnIdentityComponent, TransformComponent>();
        while (query.MoveNext(out _, out var identity, out var xform))
        {
            if (xform.MapID == map && identity.Key == new PoiSpawnKey(true, id) &&
                identity.NebulaCandidate == candidate)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Load once, then place the actual rotated grid bounds. The filter preserves nebula-specific
    /// constraints. Unplaceable grids are deleted before station registration and never returned.
    /// </summary>
    public bool TryLoadRelativeGrid(MapId map, ResPath path, RelativePoiPlacementPrototype rule,
        float clearance, Func<Vector2, bool>? filter, out Entity<MapGridComponent>? grid)
    {
        grid = null;
        var anchors = FindAnchors(map, rule);
        if (anchors.Count == 0)
            return false;

        _random.Shuffle(anchors);
        var retries = Math.Max(1, _configuration.GetCVar(NFCCVars.POIPlacementRetries));
        Entity<MapGridComponent>? loaded = null;
        for (var attempt = 0; attempt < retries; attempt++)
        {
            var anchor = anchors[attempt % anchors.Count];
            if (Deleted(anchor) || EntityManager.IsQueuedForDeletion(anchor))
                continue;

            var anchorXform = Transform(anchor);
            var anchorCenter = TryComp<MapGridComponent>(anchor, out var anchorGrid)
                ? _transform.GetWorldMatrix(anchorXform).TransformBox(anchorGrid.LocalAABB).Center
                : _transform.GetWorldPosition(anchorXform);
            // Uniform area distribution across the annulus; double avoids overflow when squaring.
            var minSquared = (double) rule.MinDistance * rule.MinDistance;
            var maxSquared = (double) rule.MaxDistance * rule.MaxDistance;
            var radius = (float) Math.Sqrt(minSquared + _random.NextFloat() * (maxSquared - minSquared));
            var position = anchorCenter + _random.NextAngle().RotateVec(new Vector2(radius, 0));
            if (filter != null && !filter(position))
                continue;

            var ev = new RelativePoiPositionAttemptEvent(map, position, _transform.GetWorldPosition(anchorXform), clearance);
            RaiseLocalEvent(ref ev);
            if (ev.Cancelled)
                continue;

            if (loaded == null && !_loader.TryLoadGrid(map, path, out loaded, offset: position, rot: _random.NextAngle()))
            {
                Log.Error($"Relative POI {rule.ID}: failed to load {path}.");
                return false;
            }

            if (loaded is not { } candidate)
                return false;

            var xform = Transform(candidate.Owner);
            var rotation = _transform.GetWorldRotation(xform);
            _transform.SetWorldPosition(candidate.Owner, position - rotation.RotateVec(candidate.Comp.LocalAABB.Center));
            if (!IsClear(map, candidate, anchor, position, clearance))
                continue;

            grid = candidate;
            var anchorName = rule.AnchorPlanet is { } planet ? $"planet {planet}" : Anchor(rule).ToString();
            Log.Info($"Relative POI {rule.ID}: placed at {Vector2.Distance(position, anchorCenter):0} m from {anchorName}.");
            return true;
        }

        if (loaded is { } failed)
        {
            _transform.DetachEntity(failed.Owner);
            QueueDel(failed.Owner);
        }
        Log.Error($"Relative POI {rule.ID}: no valid position in {rule.MinDistance}–{rule.MaxDistance} m after {retries} attempts; skipped.");
        return false;
    }

    private bool IsClear(MapId map, Entity<MapGridComponent> candidate, EntityUid anchor, Vector2 position, float clearance)
    {
        var bounds = _transform.GetWorldMatrix(candidate.Owner).TransformBox(candidate.Comp.LocalAABB);
        var protectedBounds = bounds.Enlarged(MathF.Max(0, clearance));
        var minimumSeparation = MathF.Max(0, _configuration.GetCVar(NFCCVars.MinPOIDistance)) *
                                MathF.Max(0.1f, _configuration.GetCVar(NFCCVars.POIDistanceModifier));
        var query = AllEntityQuery<MapGridComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var otherGrid, out var xform))
        {
            if (uid == candidate.Owner || xform.MapID != map || Deleted(uid) || EntityManager.IsQueuedForDeletion(uid))
                continue;

            var otherBounds = _transform.GetWorldMatrix(xform).TransformBox(otherGrid.LocalAABB);
            if (protectedBounds.Intersects(otherBounds))
                return false;

            if (!TryComp<PoiSpawnIdentityComponent>(uid, out var identity))
                continue;

            var separation = MathF.Max(0, clearance) + identity.Clearance;
            if (uid != anchor)
                separation = MathF.Max(separation, minimumSeparation);
            if (Vector2.DistanceSquared(position, otherBounds.Center) < separation * separation)
                return false;
        }
        return true;
    }

    private List<EntityUid> FindAnchors(MapId map, RelativePoiPlacementPrototype rule)
    {
        var result = new List<EntityUid>();
        if (rule.AnchorPlanet is { } planet)
        {
            // Include paused planets: POIs are placed before the main map is initialized.
            var planets = AllEntityQuery<PlanetMarkerComponent, TransformComponent>();
            while (planets.MoveNext(out var uid, out var marker, out var xform))
            {
                if (marker.Planet == planet && xform.MapID == map &&
                    !Deleted(uid) && !EntityManager.IsQueuedForDeletion(uid))
                    result.Add(uid);
            }
            return result;
        }

        var key = Anchor(rule);
        var query = AllEntityQuery<PoiSpawnIdentityComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var identity, out var xform))
        {
            if (identity.Key == key && xform.MapID == map && !Deleted(uid) && !EntityManager.IsQueuedForDeletion(uid))
                result.Add(uid);
        }
        return result;
    }

    private static PoiSpawnKey Target(RelativePoiPlacementPrototype rule) => rule.Poi is { } poi
        ? new PoiSpawnKey(false, poi.Id)
        : new PoiSpawnKey(true, rule.NebulaPoi!.Value.Id);

    private static PoiSpawnKey Anchor(RelativePoiPlacementPrototype rule) => rule.AnchorPoi is { } poi
        ? new PoiSpawnKey(false, poi.Id)
        : new PoiSpawnKey(true, rule.AnchorNebulaPoi!.Value.Id);
}
