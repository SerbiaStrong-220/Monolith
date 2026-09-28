using Content.Shared._Exodus.Virology;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Atmos;
using System.Numerics;
using Content.Shared._Exodus.Virology.Intelligent;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class VirusLifecycleSystem
{
    private readonly Dictionary<(EntityUid Host, string Strain), (VirusDescriptor Descriptor, float Chance)> _exposures = [];
    private readonly HashSet<(EntityUid Host, string Strain)> _ineligibleReservoirHosts = [];
    [Dependency] private SharedMapSystem _reservoirMaps = default!;
    private readonly List<MapCoordinates> _reservoirPerimeter = [];

    private void InitializeReservoirs()
    {
        SubscribeLocalEvent<VirusReservoirComponent, MapInitEvent>(OnReservoirInit);
    }

    private void OnReservoirInit(Entity<VirusReservoirComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Strain == null && ent.Comp.InitialVirus is { } virus)
            ent.Comp.Strain = _virology.BuildDescriptor(virus);
        if (ent.Comp.Strain is { } strain)
            ent.Comp.Identity = _virology.GetIdentity(strain);
    }

    private void ExposeReservoirs()
    {
        _exposures.Clear();
        _ineligibleReservoirHosts.Clear();
        var query = EntityQueryEnumerator<VirusReservoirComponent>();
        while (query.MoveNext(out var uid, out var reservoir))
        {
            if (reservoir.Strain is not { } strain || reservoir.InfectionChance <= 0f
                || TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid)
                || _containers.IsEntityInContainer(uid))
                continue;

            _nearby.Clear();
            var extent = new Vector2(Math.Max(0, reservoir.Footprint.X - 1), Math.Max(0, reservoir.Footprint.Y - 1));
            _lookup.GetEntitiesInRange(_transform.GetMapCoordinates(uid), reservoir.Range + extent.Length() + 0.75f, _nearby);
            if (_nearby.Count == 0)
                continue;

            reservoir.Identity ??= _virology.GetIdentity(strain);
            var atmosphereChecked = false;
            _reservoirPerimeter.Clear();
            foreach (var (host, _) in _nearby)
            {
                if (_mobState.IsDead(host) || _containers.IsEntityInContainer(host))
                    continue;

                var key = (host, reservoir.Identity);
                // Check overlapping sources only if they could increase this host's exposure.
                if (_ineligibleReservoirHosts.Contains(key)
                    || _exposures.TryGetValue(key, out var previous) && previous.Chance >= reservoir.InfectionChance)
                    continue;

                if (!_virology.CanAcquireVirus(host, strain))
                {
                    _ineligibleReservoirHosts.Add(key);
                    continue;
                }

                // Unoccupied or already infected areas need no atmosphere or obstruction checks.
                if (!atmosphereChecked)
                {
                    if (reservoir.Footprint.X > 0 && reservoir.Footprint.Y > 0)
                        GatherPerimeter(uid, reservoir.Footprint);
                    else if (_atmos.GetContainingMixture(uid) is { } air && air.Pressure >= Atmospherics.HazardLowPressure)
                        _reservoirPerimeter.Add(_transform.GetMapCoordinates(uid));
                    if (_reservoirPerimeter.Count == 0)
                        break;

                    atmosphereChecked = true;
                }

                var target = _transform.GetMapCoordinates(host);
                foreach (var point in _reservoirPerimeter)
                {
                    if (!_interaction.InRangeUnobstructed(point, target, reservoir.Range, predicate: entity => entity == uid || entity == host))
                        continue;
                    _exposures[key] = (strain, reservoir.InfectionChance);
                    break;
                }
            }
        }

        foreach (var (key, exposure) in _exposures)
            _virology.TryExpose(key.Host, exposure.Descriptor, VirusTransmissionVector.Proximity, exposure.Chance);
    }

    private void GatherPerimeter(EntityUid uid, Vector2i size)
    {
        var xform = Transform(uid);
        if (xform.GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return;
        var origin = _reservoirMaps.TileIndicesFor(grid, mapGrid, xform.Coordinates);
        var rotation = RotGeometry.QuarterTurns(xform.LocalRotation);
        for (var x = 0; x < size.X; x++)
        {
            AddPerimeterPoint(origin, new Vector2i(x, 0), Vector2i.Down);
            AddPerimeterPoint(origin, new Vector2i(x, size.Y - 1), Vector2i.Up);
        }
        for (var y = 0; y < size.Y; y++)
        {
            AddPerimeterPoint(origin, new Vector2i(0, y), Vector2i.Left);
            AddPerimeterPoint(origin, new Vector2i(size.X - 1, y), Vector2i.Right);
        }

        void AddPerimeterPoint(Vector2i start, Vector2i cell, Vector2i direction)
        {
            var adjacent = start + RotGeometry.Rotate(cell + direction, rotation);
            if (_atmos.GetTileMixture(grid, xform.MapUid, adjacent) is not { } air || air.Pressure < Atmospherics.HazardLowPressure)
                return;
            var tile = start + RotGeometry.Rotate(cell, rotation);
            var outward = RotGeometry.Rotate(direction, rotation);
            var point = _reservoirMaps.GridTileToLocal(grid, mapGrid, tile)
                .Offset(new Vector2(outward.X, outward.Y) * (mapGrid.TileSize * 0.51f));
            _reservoirPerimeter.Add(_transform.ToMapCoordinates(point));
        }
    }
}
