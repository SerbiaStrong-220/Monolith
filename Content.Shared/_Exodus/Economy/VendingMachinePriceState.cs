// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Economy;

[Serializable, NetSerializable]
public sealed class VendingMachinePriceState(Dictionary<EntProtoId, int?> prices) : BoundUserInterfaceState
{
    /// <summary>
    /// Current server quotes. A null price means the product cannot safely be priced.
    /// </summary>
    public readonly Dictionary<EntProtoId, int?> Prices = prices;
}
