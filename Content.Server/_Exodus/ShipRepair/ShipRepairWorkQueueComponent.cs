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
    public readonly Dictionary<Vector2i, List<ShipRepairTarget>> EntriesByTile = new();
    public readonly Dictionary<ShipRepairTarget, ShipRepairStage> TargetStages = new();
    public readonly Dictionary<int, ShipRepairStage> PaletteStages = new();
    public readonly Dictionary<ShipRepairStage, ShipRepairStageQueue> Stages = new();
    public int StageRetryRevision;
    /// <summary>Only newly discovered damage can preempt travel to an already assigned later stage.</summary>
    public int WorkRevision;

    /// <summary>Retry deferred stages even without a geometry change. Runtime only.</summary>
    [AutoPausedField]
    public TimeSpan NextStageRetry;

    public readonly Dictionary<ShipRepairTarget, EntityUid> Reservations = new();
    public readonly Dictionary<EntityUid, EntityUid> ClearableReservations = new();

    /// <summary>Destination cells reserved for repair or temporarily giving way.</summary>
    public readonly Dictionary<Vector2i, EntityUid> WorkPositions = new();

    public int NavigationRevision;
    public readonly List<ShipRepairUnreachableRegion> Unreachable = new();

    /// <summary>Snapshot targets whose live damage changed since the last queue update.</summary>
    public readonly HashSet<ShipRepairTarget> DirtyTargets = new();

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

/// <summary>Only damaged/missing entries; swap removal keeps a bounded candidate scan cheap.</summary>
public sealed class ShipRepairStageQueue
{
    public readonly List<ShipRepairTarget> Targets = new();
    public readonly Dictionary<ShipRepairTarget, int> Indices = new();
    public readonly Dictionary<Vector2i, int> TileCounts = new();
    public readonly HashSet<ShipRepairTarget> Reserved = new();
    public int Revision;
    /// <summary>Grid work revision when a new damaged entry was last added to this stage.</summary>
    public int DiscoveryRevision;
}

/// <summary>A drone's incremental eligibility pass. Exhaustion defers work, never declares it repaired.</summary>
public sealed class ShipRepairStageProbe
{
    public int Revision = -1;
    public int SnapshotRevision = -1;
    public int RetryRevision = -1;
    public int Cursor;
    public int Remaining;
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
    public float StructuralRepairTileRange;
    public float ExteriorMargin;
}
