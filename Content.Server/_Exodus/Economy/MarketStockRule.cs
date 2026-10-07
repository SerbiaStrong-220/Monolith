// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Shared.Whitelist;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// An additional station stock admission rule with its own exclusions.
/// </summary>
[DataDefinition]
public sealed partial class MarketStockRule
{
    /// <summary>
    /// Sold entities must match this whitelist to enter stock through this rule.
    /// </summary>
    [DataField(required: true)]
    public EntityWhitelist Whitelist = new();

    /// <summary>
    /// Matching entities are excluded even when this rule's whitelist accepts them.
    /// </summary>
    [DataField]
    public EntityWhitelist? Blacklist;
}
