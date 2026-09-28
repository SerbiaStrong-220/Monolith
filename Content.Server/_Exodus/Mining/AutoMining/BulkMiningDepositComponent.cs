namespace Content.Server._Exodus.Mining.AutoMining;

/// <summary>
/// Finite bulk-mining rights granted only to the original tiles of a generated resource grid.
/// Saved with the grid so loading it cannot replenish excavated or replaced tiles.
/// </summary>
[RegisterComponent, Access(typeof(BulkMiningDepositSystem))]
public sealed partial class BulkMiningDepositComponent : Component
{
    /// <summary>Whether generation has already granted this grid its one-time deposit.</summary>
    [DataField]
    public bool Captured;

    /// <summary>
    /// Sparse 8-by-8 tile blocks. Each bit represents one remaining original tile.
    /// Coordinates use arithmetic shifts, including for negative grid indices.
    /// </summary>
    [DataField]
    public Dictionary<Vector2i, ulong> Blocks = new();
}
