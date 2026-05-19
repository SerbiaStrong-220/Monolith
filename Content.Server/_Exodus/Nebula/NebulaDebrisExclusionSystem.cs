using System.Numerics;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Systems.Debris;
using Content.Shared._Exodus.Nebula;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Suppresses worldgen debris (asteroids etc.) inside nebulas whose marker prototype carries
/// <see cref="NebulaBlocksDebrisComponent"/>. Listens to <c>PrePlaceDebrisFeatureEvent</c>
/// raised by <see cref="DebrisFeaturePlacerSystem"/> before each spawn and cancels it.
///
/// Bluespace events and other dynamic spawners are not affected — this only gates the
/// chunk-driven worldgen pipeline.
/// </summary>
public sealed class NebulaDebrisExclusionSystem : EntitySystem
{
    [Dependency] private readonly IComponentFactory _componentFactory = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly SharedMapSystem _mapSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private readonly HashSet<string> _debrisBlockerMarkers = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DebrisFeaturePlacerControllerComponent, PrePlaceDebrisFeatureEvent>(OnPrePlaceDebris);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        BuildCache();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>())
            BuildCache();
    }

    private void BuildCache()
    {
        _debrisBlockerMarkers.Clear();

        foreach (var proto in _prototype.EnumeratePrototypes<EntityPrototype>())
        {
            if (!proto.TryGetComponent<NebulaComponent>(out _, _componentFactory))
                continue;

            if (proto.TryGetComponent<NebulaBlocksDebrisComponent>(out _, _componentFactory))
                _debrisBlockerMarkers.Add(proto.ID);
        }
    }

    private void OnPrePlaceDebris(EntityUid uid, DebrisFeaturePlacerControllerComponent component, ref PrePlaceDebrisFeatureEvent args)
    {
        if (args.Handled || _debrisBlockerMarkers.Count == 0)
            return;

        var mapCoords = _transform.ToMapCoordinates(args.Coords);
        if (mapCoords.MapId == MapId.Nullspace)
            return;

        if (!_mapSystem.TryGetMap(mapCoords.MapId, out var mapUid))
            return;

        if (!TryComp<NebulaMapComponent>(mapUid, out var mapComponent))
            return;

        if (IsBlockedAt(mapCoords.Position, mapComponent))
            args.Handled = true;
    }

    /// <summary>
    /// True if <paramref name="position"/> is inside a nebula whose marker has
    /// <see cref="NebulaBlocksDebrisComponent"/>. Both blob nebulas and death-zone sub-zones
    /// are checked.
    /// </summary>
    private bool IsBlockedAt(Vector2 position, NebulaMapComponent mapComponent)
    {
        for (var i = 0; i < mapComponent.Nebulas.Count; i++)
        {
            if (i >= mapComponent.NebulaPrototypes.Count)
                continue;

            var marker = mapComponent.NebulaPrototypes[i];
            if (marker.Id == null || !_debrisBlockerMarkers.Contains(marker.Id))
                continue;

            var nebula = mapComponent.Nebulas[i];
            var delta = position - nebula.Center;
            if (delta.LengthSquared() > nebula.BoundingRadius * nebula.BoundingRadius)
                continue;

            if (nebula.Contains(position))
                return true;
        }

        if (!mapComponent.WorldEnd.IsGenerated || !mapComponent.WorldEnd.TryGetZone(position, out var zone))
            return false;

        var zoneMarker = zone == WorldEndZone.Outer
            ? mapComponent.WorldEndOuterMarker
            : mapComponent.WorldEndInnerMarker;

        return zoneMarker.Id != null && _debrisBlockerMarkers.Contains(zoneMarker.Id);
    }
}
