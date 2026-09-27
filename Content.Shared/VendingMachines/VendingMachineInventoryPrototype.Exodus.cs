// Exodus-begin configurable recipients for the full price of individual products.
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared.VendingMachines;

public sealed partial class VendingMachineInventoryPrototype
{
    /// <summary>Overrides the machine's tax split for these products.</summary>
    [DataField]
    public Dictionary<EntProtoId, SectorBankAccount> RevenueAccounts = new();
}
// Exodus-end
