// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Exodus.Economy;

/// <summary>
/// A configurable commodity group, independent of individual market keys and their price factors.
/// </summary>
[Prototype]
public sealed partial class MarketCommodityGroupPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Localized group name.
    /// </summary>
    [DataField(required: true)]
    public LocId Name { get; private set; } = default!;

    /// <summary>
    /// Use this group when no classification rule matches. Exactly one group must be the default.
    /// </summary>
    [DataField]
    public bool Default { get; private set; }

    /// <summary>
    /// Use this group for all gas commodity keys, separately from their containers.
    /// Exactly one group must have this flag.
    /// </summary>
    [DataField]
    public bool Gases { get; private set; }

    /// <summary>
    /// Multiplier for buy and sell price pressure per market unit (mole for gases).
    /// Must be finite and non-negative. Zero disables trade pressure for this group.
    /// </summary>
    [DataField]
    public float ImpactMultiplier { get; private set; } = 1f;
}
