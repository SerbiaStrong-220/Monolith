using Content.Server._Exodus.Economy; // Exodus: configurable additional stock rules.
using Content.Server._NF.Market.Systems;
using Content.Shared.Whitelist;

namespace Content.Server._NF.Market.Components;

/// <summary>
/// Exodus: optional local admission rules for the server-wide market inventory.
/// </summary>
[RegisterComponent]
[Access(typeof(MarketSystem), typeof(MarketStockIntakeSystem))] // Exodus: central intake owns admission.
public sealed partial class CargoMarketDataComponent : Component
{
    // Exodus: stock belongs to MarketInventoryComponent and is shared by all sale endpoints.

    /// <summary>
    /// Sold items must match this whitelist to enter into this data set.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Sold items not must match this blacklist to enter into this data set.
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;

    /// <summary>
    /// Particular items that may override the blacklist.
    /// </summary>
    [DataField]
    public EntityWhitelist? WhitelistOverride;

    // Exodus-begin: additional admission paths retain their own blacklist.
    /// <summary>
    /// Additional rules that can admit sold entities when the standard filters reject them.
    /// Each rule must pass its whitelist and blacklist independently.
    /// </summary>
    [DataField]
    public List<MarketStockRule> AdditionalStockRules = [];
    // Exodus-end
}
