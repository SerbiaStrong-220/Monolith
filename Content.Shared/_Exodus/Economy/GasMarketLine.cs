// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Economy;

/// <summary>
/// One gas species line for gas-sale console UI (shared with dynamic market keys gas:*).
/// </summary>
[Serializable, NetSerializable]
public sealed class GasMarketLine
{
    /// <summary>
    /// Gas enum index (Content.Shared.Atmos.Gas).
    /// </summary>
    public int GasId;

    /// <summary>
    /// Moles of this gas in the sale mixture.
    /// </summary>
    public float Moles;

    /// <summary>
    /// Effective average unit price after sector factor, purity and console modifier.
    /// </summary>
    public double UnitPrice;

    /// <summary>
    /// Contribution to total appraisal after console MarketModifier.
    /// </summary>
    public int LineTotal;

    public float Trend;
    public double ChangePercent;
}
