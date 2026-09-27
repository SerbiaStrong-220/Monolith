// Exodus-begin paid loadout revenue.
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;

namespace Content.Server.Station.Systems;

public sealed partial class StationSpawningSystem
{
    private void DepositLoadoutRevenue(Dictionary<SectorBankAccount, int>? revenue)
    {
        if (revenue == null)
            return;

        foreach (var (account, amount) in revenue)
        {
            if (!_bank.TrySectorDeposit(account, amount, LedgerEntryType.ProductSales))
                Log.Error($"Could not credit {amount} of loadout revenue to {account}.");
        }
    }
}
// Exodus-end
