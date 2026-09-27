// Exodus-begin configurable recipient of a paid loadout's price.
using Content.Shared._NF.Bank.Components;

namespace Content.Shared.Preferences.Loadouts;

public sealed partial class LoadoutPrototype
{
    /// <summary>Receives the price only after the purchased loadout has been charged successfully.</summary>
    [DataField]
    public SectorBankAccount? RevenueAccount;
}
// Exodus-end
