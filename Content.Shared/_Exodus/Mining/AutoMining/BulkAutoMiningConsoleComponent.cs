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

    [ViewVariables]
    public List<EntityUid> SelectedGrids = new();

    [ViewVariables]
    public bool Active;

    [ViewVariables]
    public int ProcessedTiles;

    [ViewVariables]
    public int TotalTiles;
}
