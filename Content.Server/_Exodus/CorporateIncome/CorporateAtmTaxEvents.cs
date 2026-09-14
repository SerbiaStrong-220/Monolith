using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.CorporateIncome;

/// <summary>Resolves an ATM's corporate taxes without changing the prototype's default tax accounts.</summary>
[ByRefEvent]
public record struct GetCorporateAtmTaxEvent(int Deposit)
{
    public CorporateAtmTaxQuote? Quote;
}

/// <summary>Immutable recipient snapshot used for both deduction and payment of a successful deposit.</summary>
public readonly record struct CorporateAtmTaxQuote(
    EntityUid Ledger,
    ProtoId<CompanyPrototype> Company,
    int CompanyAmount,
    IReadOnlyDictionary<SectorBankAccount, int> SectorAmounts,
    int Total);

/// <summary>Raised only after the depositor's bank transaction succeeds.</summary>
[ByRefEvent]
public readonly record struct CorporateAtmTaxPaidEvent(CorporateAtmTaxQuote Quote);
