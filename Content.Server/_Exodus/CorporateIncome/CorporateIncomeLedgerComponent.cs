using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.CorporateIncome;

/// <summary>Round-local funds independent of territory grids, banners, and player bodies.</summary>
[RegisterComponent]
public sealed partial class CorporateIncomeLedgerComponent : Component
{
    /// <summary>Whether this round is accepting income and paying salaries.</summary>
    [ViewVariables]
    public bool Active;

    /// <summary>Next scheduled transfer to corporate bank accounts.</summary>
    [ViewVariables]
    public TimeSpan NextTreasuryPayout;

    /// <summary>Next scheduled distribution to online members.</summary>
    [ViewVariables]
    public TimeSpan NextSalaryPayout;

    /// <summary>Accrued funds and influence points belonging to each corporation.</summary>
    public readonly Dictionary<ProtoId<CompanyPrototype>, CorporateIncomeAccount> Accounts = new();

    /// <summary>Tracked claims, allowing removals and duplicate notifications to be processed once.</summary>
    public readonly Dictionary<EntityUid, CorporateIncomeClaim> Claims = new();

    /// <summary>ATM taxes awaiting a successful transfer to their sector accounts.</summary>
    public readonly Dictionary<SectorBankAccount, long> PendingSectorTaxes = new();
}

public sealed class CorporateIncomeAccount
{
    /// <summary>Influence points currently earning passive income.</summary>
    public int Points;

    /// <summary>Time through which passive income has been calculated.</summary>
    public TimeSpan LastAccrual;

    /// <summary>Unpaid passive credits.</summary>
    public decimal PassiveIncome;

    /// <summary>Fractional income numerator, in credit-ticks, carried between accruals.</summary>
    public decimal AccrualRemainder;

    /// <summary>Unpaid credits collected from ATM deposits.</summary>
    public long AtmIncome;
}

public readonly record struct CorporateIncomeClaim(ProtoId<CompanyPrototype> Company, int Points);
