// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Shipyard;

/// <summary>
/// Initial ship equipment counted by commodity type. The purchase capture is immutable after finalization;
/// each sale consumes a separate copy rather than changing these baseline quantities.
/// </summary>
[RegisterComponent]
public sealed partial class ShipyardOriginalContentsComponent : Component
{
    /// <summary>
    /// Initial physical and virtual contents, including installed parts, using canonical commodity keys.
    /// Serialized so a saved ship retains its original equipment allowance.
    /// </summary>
    [DataField]
    public Dictionary<string, double> StockQuantities = new();

    /// <summary>
    /// Initial live basket quantities by market key, independently of stock admission and sale prices.
    /// Serialized separately because sale pressure also includes goods that cannot enter resale stock.
    /// </summary>
    [DataField]
    public Dictionary<string, double> CommodityQuantities = new();

    /// <summary>Runtime guard against repeated notifications for the initial ship-loading checkpoint.</summary>
    public bool Loaded;

    /// <summary>Runtime guard allowing exactly one final capture after purchase modifiers are applied.</summary>
    public bool Finalized;
}
