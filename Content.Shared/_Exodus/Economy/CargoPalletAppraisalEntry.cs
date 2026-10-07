// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Economy;

/// <summary>
/// One grouped line on a cargo pallet appraisal (market-adjusted total).
/// </summary>
[Serializable, NetSerializable]
public sealed class CargoPalletAppraisalEntry
{
    /// <summary>
    /// Display name of the entity.
    /// </summary>
    public string Name = string.Empty;

    /// <summary>
    /// Entity prototype id for client icon (optional).
    /// </summary>
    public EntProtoId? PrototypeId;

    /// <summary>
    /// Total represented quantity. Matching non-stack entities are counted individually.
    /// </summary>
    public int Quantity = 1;

    /// <summary>
    /// Market-adjusted payout for all entities represented by this line.
    /// </summary>
    public int Price;

    /// <summary>
    /// Approximate unit price (Price / Quantity), for display.
    /// </summary>
    public double UnitPrice;
}
