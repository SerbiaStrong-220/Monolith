// (c) Space Exodus Team - EXDS-RL with CLA
namespace Content.Server._Exodus.Economy;

public sealed partial class MarketTransactionState
{
    internal Dictionary<string, ReagentSale>? ReagentSales;

    internal readonly record struct ReagentSale(double Units, double NominalValue, double MarketValue,
        double InitialFactor, double FinalFactor);
}

public sealed partial class DynamicMarketSystem
{
    private void RecordReagentSale(MarketTransactionState transaction, string key, double units,
        double nominalValue, double marketValue, double initialFactor, double finalFactor, bool committed)
    {
        if (!IsReagentKey(key))
            return;

        var sale = new MarketTransactionState.ReagentSale(units, nominalValue, marketValue, initialFactor, finalFactor);
        if (committed)
        {
            LogReagentSale(key, sale);
            return;
        }

        var sales = transaction.ReagentSales ??= new();
        if (sales.TryGetValue(key, out var previous))
        {
            sale = sale with
            {
                Units = previous.Units + units,
                NominalValue = previous.NominalValue + nominalValue,
                MarketValue = previous.MarketValue + marketValue,
                InitialFactor = previous.InitialFactor,
            };
        }
        sales[key] = sale;
    }

    private void LogReagentSale(string key, MarketTransactionState.ReagentSale sale)
    {
        // This is the market appraisal before item taxes/rewards, not a claim about cash paid to a player.
        Log.Info($"Reagent market sale: key={key}, units={sale.Units:R}, nominal={sale.NominalValue:R}, " +
                 $"marketValue={sale.MarketValue:R}, factor={sale.InitialFactor:R}->{sale.FinalFactor:R}");
    }
}
