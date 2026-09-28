using System.Numerics;
using Content.Shared._Exodus.Mining.AutoMining;

namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>Transient queues are rebuilt from the current grid when starting a job.</summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(BulkAutoMiningSystem))]
public sealed partial class BulkAutoMiningJobComponent : Component
{
    public readonly List<BulkAutoMiningGridJob> GridJobs = new();
    public readonly List<EntityUid> Emitters = new();
    public readonly Dictionary<EntityUid, BulkAutoMiningLaserStatus> Statuses = new();

    /// <summary>Search work left for aiming after all emitters have finished excavating.</summary>
    public readonly Dictionary<EntityUid, int> TileChecksRemaining = new();
    public int NextGridIndex;

    /// <summary>Round-robin position among grid/emitter pairs for bounded range searches.</summary>
    public int NextRangePairIndex;

    [AutoPausedField]
    public TimeSpan NextRangeCheckTime;

    [AutoPausedField]
    public TimeSpan NextProcessTime;

    [AutoPausedField]
    public TimeSpan NextBeamCheckTime;

    [AutoPausedField]
    public TimeSpan NextUiTime;
}

public sealed class BulkAutoMiningGridJob
{
    public EntityUid GridUid;

    /// <summary>Lost before completion. Retain the entry to keep both search cursors stable.</summary>
    public bool Invalidated;

    public Queue<Vector2i> Tiles = new();
    public HashSet<Vector2i> RemainingTiles = new();

    /// <summary>Immutable tile order, allowing range searches to resume while mining removes remaining tiles.</summary>
    public List<Vector2i> RangeTiles = new();

    /// <summary>One search per emitter in the job's fixed emitter list.</summary>
    public BulkAutoMiningRangeSearch[] RangeSearches = [];
}

public struct BulkAutoMiningRangeSearch
{
    public Vector2i? CachedTile;
    public int NextTileIndex;
    public Vector2 Origin;
    public float MinimumDistanceSquared;
}
