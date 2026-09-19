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
    public readonly Dictionary<EntityUid, EntityUid> ClearableReservations = new();

    /// <summary>Destination cells reserved for repair or temporarily giving way.</summary>
    public readonly Dictionary<Vector2i, EntityUid> WorkPositions = new();

    public int NavigationRevision;
    public readonly List<ShipRepairUnreachableRegion> Unreachable = new();

    /// <summary>Fallback invalidation for environmental changes without a geometry event.</summary>
    [DataField, AutoPausedField]
    public TimeSpan NextNavigationRetry;

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

/// <summary>A fully explored side of a failed search, not a timeout. Bounded and discarded on geometry changes.</summary>
public sealed class ShipRepairUnreachableRegion
{
    public required ShipRepairTarget Target;
    public required HashSet<Vector2i> Tiles;
    public bool FromTarget;
    public float Clearance;
    public float BodyRadius;
    public float RepairRange;
    public int RepairRadius;
    public float ExteriorMargin;
}
