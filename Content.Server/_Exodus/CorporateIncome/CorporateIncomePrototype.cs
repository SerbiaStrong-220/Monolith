using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.CorporateIncome;

/// <summary>Rates and destinations shared by corporate territory income and ATM taxes.</summary>
[Prototype]
public sealed partial class CorporateIncomePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Credits earned per influence point over one income interval.</summary>
    [DataField]
    public int IncomePerPoint = 67000;

    /// <summary>Time represented by IncomePerPoint.</summary>
    [DataField]
    public TimeSpan IncomeInterval = TimeSpan.FromHours(1);

    /// <summary>Interval between transfers of accrued income to corporate bank accounts.</summary>
    [DataField]
    public TimeSpan TreasuryInterval = TimeSpan.FromMinutes(1);

    /// <summary>Interval between equal payments to active members of companies without a bank account.</summary>
    [DataField]
    public TimeSpan SalaryInterval = TimeSpan.FromHours(1);

    /// <summary>Fraction of an ATM deposit credited to its grid's corporate controller.</summary>
    [DataField]
    public float AtmCompanyRate = 0.04f;

    /// <summary>Additional fractions of an ATM deposit paid to sector accounts on corporate territory.</summary>
    [DataField]
    public Dictionary<SectorBankAccount, float> AtmTaxAccounts = new();

    /// <summary>Companies whose income goes to an existing bank account instead of member salaries.</summary>
    [DataField]
    public Dictionary<ProtoId<CompanyPrototype>, SectorBankAccount> BankAccounts = new();

    /// <summary>Companies funded only by their existing subsidies and sector taxes.</summary>
    [DataField]
    public HashSet<ProtoId<CompanyPrototype>> ExcludedCompanies = new();
}
