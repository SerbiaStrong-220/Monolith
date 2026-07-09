using System.Numerics;
using Content.Server._Exodus.Nebula.Components;
using Content.Server._Exodus.Nebula.Presence;
using Content.Server.Worldgen.Components.Debris;
using Content.Server.Worldgen.Systems.Debris;
using Content.Shared._Exodus.Nebula.Components;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula.Generation;

/// <summary>
/// Suppresses worldgen debris (asteroids etc.) inside nebulas whose marker prototype carries
/// <see cref="NebulaBlocksDebrisComponent"/>. Listens to <c>PrePlaceDebrisFeatureEvent</c>
/// raised by <see cref="DebrisFeaturePlacerSystem"/> before each spawn and cancels it.
///
/// Bluespace events and other dynamic spawners are not affected — this only gates the
/// chunk-driven worldgen pipeline.
/// </summary>
public sealed partial class NebulaDebrisExclusionSystem : EntitySystem
{
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

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
    /// are covered by <see cref="NebulaQueryHelper.TryGetMarkerAt"/>.
    /// </summary>
    private bool IsBlockedAt(Vector2 position, NebulaMapComponent mapComponent)
    {
        return NebulaQueryHelper.TryGetMarkerAt(mapComponent, position, out var marker) &&
               marker.Id != null &&
               _debrisBlockerMarkers.Contains(marker.Id);
    }
}
