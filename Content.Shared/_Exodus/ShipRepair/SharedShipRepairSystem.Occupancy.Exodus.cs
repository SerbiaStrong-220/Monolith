using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.ShipRepair;

public abstract partial class SharedShipRepairSystem
{
    // Temporary matching for one tile, not a persistent cache of entity state.
    private readonly List<EntityUid> _repairTileOccupants = new();
    private readonly List<KeyValuePair<int, ShipRepairEntitySpecifier>> _repairTileEntries = new();
    private readonly Dictionary<EntityUid, int> _repairSnapshotOccupants = new();
    private readonly HashSet<int> _repairOccupiedSlots = new();

    private void CollectRepairSnapshotOccupants(Entity<ShipRepairDataComponent> grid, MapGridComponent mapGrid,
        Vector2i tile, ShipRepairChunk chunk)
    {
        _repairTileOccupants.Clear();
        _repairTileEntries.Clear();
        _repairSnapshotOccupants.Clear();
        _repairOccupiedSlots.Clear();
        foreach (var uid in _map.GetAnchoredEntities(grid, mapGrid, tile))
        {
            if (!TerminatingOrDeleted(uid))
                _repairTileOccupants.Add(uid);
        }
        if (_repairTileOccupants.Count == 0)
            return;

        foreach (var entry in chunk.Entities)
        {
            if (_map.LocalToTile(grid, mapGrid, new EntityCoordinates(grid, entry.Value.LocalPosition)) == tile)
                _repairTileEntries.Add(entry);
        }

        // Surviving originals and our restored entities own their exact snapshot entry first.
        foreach (var (id, spec) in _repairTileEntries)
        {
            if (spec.OriginalEntity is not { } netEntity || !TryGetEntity(netEntity, out var original) ||
                original is not { } uid || !_repairTileOccupants.Contains(uid) ||
                !MatchesRepairPose(grid, uid, spec) || !_repairSnapshotOccupants.TryAdd(uid, id))
                continue;
            _repairOccupiedSlots.Add(id);
        }

        // A manually installed object can fill one remaining entry, never every overlapping copy.
        foreach (var uid in _repairTileOccupants)
        {
            if (_repairSnapshotOccupants.ContainsKey(uid))
                continue;
            foreach (var (id, spec) in _repairTileEntries)
            {
                if (_repairOccupiedSlots.Contains(id) || !MatchesRepairPose(grid, uid, spec) ||
                    !MatchesRepairPrototype(grid.Comp, uid, spec))
                    continue;
                _repairSnapshotOccupants.Add(uid, id);
                _repairOccupiedSlots.Add(id);
                break;
            }
        }
    }

    private bool MatchesRepairPose(EntityUid grid, EntityUid uid, ShipRepairEntitySpecifier spec)
    {
        return _repairTransformQuery.TryGetComponent(uid, out var xform) && xform.Anchored &&
               xform.ParentUid == grid && Vector2.DistanceSquared(xform.LocalPosition, spec.LocalPosition) < 0.0001f &&
               Math.Abs(Angle.ShortestDistance(xform.LocalRotation, spec.Rotation).Theta) < 0.01;
    }

    private bool MatchesRepairPrototype(ShipRepairDataComponent data, EntityUid uid, ShipRepairEntitySpecifier spec)
    {
        if (spec.ProtoIndex < 0 || spec.ProtoIndex >= data.EntityPalette.Count ||
            !_repairMetadataQuery.TryGetComponent(uid, out var metadata) || metadata.EntityPrototype is not { } actual)
            return false;

        var expected = data.EntityPalette[spec.ProtoIndex];
        var replacement = _repairableQuery.TryGetComponent(uid, out var repairable) ? repairable.RepairTo : null;
        if (actual.ID == expected.Id || replacement == expected)
            return true;

        // Older snapshots can still name the filled prototype, while a current repair uses its empty variant.
        return _proto.TryIndex(expected, out var prototype) &&
               prototype.TryGetComponent<ShipRepairableComponent>(out var source, Factory) &&
               source.RepairTo is { } current && (actual.ID == current.Id || replacement == current);
    }

    /// <summary>Whether an anchored obstruction is another intended object on this reconstruction's tile.</summary>
    public bool IsRepairSnapshotNeighbour(EntityUid grid, ShipRepairTarget target, EntityUid uid)
    {
        if (target.EntityId is not { } id || TerminatingOrDeleted(grid) || TerminatingOrDeleted(uid) ||
            _repairMobQuery.HasComponent(uid) || !TryComp<ShipRepairDataComponent>(grid, out var data) ||
            data.ChunkSize <= 0 || !_repairGridQuery.TryGetComponent(grid, out var mapGrid) ||
            !_repairTransformQuery.TryGetComponent(uid, out var xform) || !xform.Anchored ||
            xform.ParentUid != grid || _map.LocalToTile(grid, mapGrid, xform.Coordinates) != target.Tile ||
            !TryGetChunk(data, target.Tile, out var chunk))
            return false;

        CollectRepairSnapshotOccupants((grid, data), mapGrid, target.Tile, chunk);
        return _repairSnapshotOccupants.TryGetValue(uid, out var other) && other != id;
    }
}
