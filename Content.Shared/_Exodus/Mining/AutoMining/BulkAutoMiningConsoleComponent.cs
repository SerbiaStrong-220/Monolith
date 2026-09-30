using Content.Shared.Whitelist;

namespace Content.Shared._Exodus.Mining.AutoMining;

/// <summary>
/// Fire control settings and runtime selection. UI state is sent only to console users.
/// </summary>
[RegisterComponent, Access(typeof(SharedBulkAutoMiningSystem))]
public sealed partial class BulkAutoMiningConsoleComponent : Component
{
    [DataField]
    public float MaxRange = 512f;

    /// <summary>Zero uses the server's bulk mining CVars.</summary>
    [DataField]
    public int TilesPerTick;

    [DataField]
    public TimeSpan ProcessInterval;

    [DataField]
    public EntityWhitelist? ClearableWhitelist;

    /// <summary>
    /// Distance, in meters, a laser adds to every candidate of a target already cut by each other laser of
    /// this console. Spreads lasers over several selected targets unless one is much closer.
    /// </summary>
    [DataField]
    public float GridBalancePenalty = 8f;

    /// <summary>How long another ship has to answer a consortium link request.</summary>
    [DataField]
    public TimeSpan LinkRequestTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Minimum time between link requests sent from this ship.</summary>
    [DataField]
    public TimeSpan LinkRequestCooldown = TimeSpan.FromSeconds(3);

    [DataField]
    public int MaxOutgoingLinkRequests = 3;

    /// <summary>Nearest link-capable ships listed by the console.</summary>
    [DataField]
    public int MaxLinkCandidates = 8;

    [ViewVariables]
    public List<EntityUid> SelectedGrids = new();

    [ViewVariables]
    public bool Active;

    [ViewVariables]
    public int ProcessedTiles;

    [ViewVariables]
    public int TotalTiles;
}
