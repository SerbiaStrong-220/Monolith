using Content.Shared._Exodus.Mining.AutoMining;

namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>Transient mining job of a console. Target surfaces are shared through <see cref="BulkMiningSurfaceComponent"/>.</summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(BulkAutoMiningSystem))]
public sealed partial class BulkAutoMiningJobComponent : Component
{
    public readonly List<BulkAutoMiningGridJob> GridJobs = new();
    public readonly List<EntityUid> Emitters = new();
    public readonly Dictionary<EntityUid, BulkAutoMiningLaserStatus> Statuses = new();

    /// <summary>Recently obstructed candidate tiles of each laser, skipped until the memory expires.</summary>
    public readonly Dictionary<EntityUid, BulkAutoMiningBlockedTiles> BlockedTiles = new();

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

    /// <summary>Lost before completion. Retain the entry to keep job indices stable.</summary>
    public bool Invalidated;

    /// <summary>Remaining target tiles last counted into the console's progress.</summary>
    public int Remaining;
}

public sealed class BulkAutoMiningBlockedTiles
{
    public readonly HashSet<(EntityUid Grid, Vector2i Tile)> Tiles = new();

    public TimeSpan Expires;
}
