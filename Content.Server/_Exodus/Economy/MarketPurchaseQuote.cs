// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// One purchased SKU. Quantity counts spawned entities, or individual stack units when StackUnits is true.
/// UnitBasePrice is the nominal catalog/stock price before the regional modifier and market pressure.
/// </summary>
public readonly record struct MarketPurchaseRequest(
    EntProtoId Prototype,
    int Quantity,
    double UnitBasePrice,
    double Modifier = 1,
    bool StackUnits = false);

/// <summary>
/// Server-side quote. Only NominalPrice may fund existing commissions; Adjustment is a currency sink.
/// Transaction is committed by the caller only after successful payment and reservation of the goods.
/// </summary>
public sealed record MarketPurchaseQuote(int NominalPrice, int TotalPrice, MarketTransactionState Transaction)
{
    public int Adjustment => TotalPrice - NominalPrice;
}
