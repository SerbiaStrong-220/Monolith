using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;

namespace Content.Server._Exodus.ShipRepair;

/// <summary>
/// One incremental snapshot scanner and reservation table per serviced grid.
/// Derived runtime data is rebuilt rather than saved in maps or replicated to clients.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class ShipRepairWorkQueueComponent : Component
{
    public int Revision = -1;
    public readonly HashSet<EntityUid> Drones = new();
    public readonly List<ShipRepairTarget> Entries = new();
    public readonly Queue<ShipRepairTarget> Pending = new();
    public readonly HashSet<ShipRepairTarget> PendingSet = new();
    public readonly Dictionary<ShipRepairTarget, EntityUid> Reservations = new();
    public Dictionary<Vector2i, ShipRepairChunk>.Enumerator Chunks;
    public Dictionary<int, ShipRepairEntitySpecifier>.Enumerator Entities;
    public ShipRepairChunk? Chunk;
    public Vector2i ChunkPosition;
    public int TileIndex;
    public int ScanIndex;
    public bool Indexed;
    public Box2 Bounds;

    [DataField, AutoPausedField]
    public TimeSpan NextScan;
}
