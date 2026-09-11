using Content.Shared._Exodus.TerritoryIncome;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.TerritoryIncome;

/// <summary>
/// Server-only round ledger. Its lifetime is independent of any terminal or player body.
/// </summary>
[RegisterComponent]
public sealed partial class TerritoryIncomeAccountComponent : Component
{
    /// <summary>Configuration of this account.</summary>
    [DataField]
    public ProtoId<TerritoryIncomePrototype> Account;

    /// <summary>Whether this ledger is accepting income and withdrawals this round.</summary>
    [DataField]
    public bool Active;

    /// <summary>Whole currency units already paid out and available to withdraw.</summary>
    [DataField]
    public int Balance;

    /// <summary>Sum of influence points from eligible held territories.</summary>
    [DataField]
    public int Points;

    /// <summary>Time through which income has already been accounted for.</summary>
    [DataField]
    public TimeSpan LastAccrual;

    /// <summary>Next fixed payment boundary.</summary>
    [DataField]
    public TimeSpan NextPayout;

    /// <summary>
    /// Holding time weighted by points and currency per point. One payout interval of this
    /// accumulated time equals one currency unit; remainders survive both payments and captures.
    /// </summary>
    [DataField]
    public TimeSpan PendingIncomeTime;

    /// <summary>Eligible controlled grids and their contribution to the current rate.</summary>
    [DataField]
    public Dictionary<EntityUid, int> Territories = new();

    /// <summary>Grids already claimed at round start; these never pay for this round.</summary>
    [DataField]
    public HashSet<EntityUid> InitialClaims = new();
}
