using Content.Server._Exodus.CorporateIncome;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;

namespace Content.Server._NF.Bank;

public sealed partial class BankSystem
{
    private int GetCompanyDepositCommission(EntityUid player, int deposit)
    {
        if (deposit <= 0 ||
            !TryComp<CompanyComponent>(player, out var company) ||
            !_prototypeManager.TryIndex<CompanyPrototype>(company.CompanyName, out var companyPrototype))
        {
            return 0;
        }

        var commission = companyPrototype.AtmDepositCommission;
        if (!float.IsFinite(commission) || commission <= 0f)
            return 0;

        var commissionAmount = Math.Floor((double)deposit * Math.Min(commission, 1f));
        return (int)Math.Min(commissionAmount, int.MaxValue);
    }

    private void UpdateDepositBreakdown(
        EntityUid player,
        Entity<BankATMComponent> atm,
        BankATMMenuInterfaceState state)
    {
        state.SectorDeposit = 0;
        state.SavingsDeposit = 0;
        state.AtmFee = 0;
        state.CompanyCommission = 0;

        if (state.Deposit <= 0)
            return;

        var depositAfterFees = GetDepositAfterFees(player,
            atm,
            state.Deposit,
            out state.CompanyCommission,
            out state.AtmFee);
        GetTaxedDepositAmount(depositAfterFees,
            state.Balance,
            out state.SectorDeposit,
            out state.SavingsDeposit);
    }

    internal int GetDepositAfterFees(
        EntityUid player,
        Entity<BankATMComponent> atm,
        int deposit,
        out int companyCommission,
        out int atmFee)
    {
        return GetDepositAfterFees(player, atm, deposit, out companyCommission, out atmFee, out _);
    }

    internal int GetDepositAfterFees(
        EntityUid player,
        Entity<BankATMComponent> atm,
        int deposit,
        out int companyCommission,
        out int atmFee,
        out CorporateAtmTaxQuote? corporateTax)
    {
        companyCommission = GetCompanyDepositCommission(player, deposit);
        var taxEvent = new GetCorporateAtmTaxEvent(deposit);
        RaiseLocalEvent(atm.Owner, ref taxEvent);
        corporateTax = taxEvent.Quote;

        if (corporateTax is { } quote)
        {
            atmFee = quote.Total;
        }
        else
        {
            var totalAtmFee = 0L;
            foreach (var taxCoeff in atm.Comp.TaxAccounts.Values)
                totalAtmFee += GetAtmDepositFee(deposit, taxCoeff);

            atmFee = (int)Math.Min(totalAtmFee, int.MaxValue);
        }

        return (int)Math.Max((long)deposit - companyCommission - atmFee, 0);
    }

    internal static int GetAtmDepositFee(int deposit, float taxCoeff)
    {
        if (deposit <= 0 || !float.IsFinite(taxCoeff) || taxCoeff <= 0f)
            return 0;

        var fee = Math.Floor(deposit * taxCoeff);
        return (int)Math.Min(fee, int.MaxValue);
    }

    private void DepositAtmTaxes(Entity<BankATMComponent> atm, int deposit, CorporateAtmTaxQuote? corporateTax)
    {
        if (corporateTax is { } quote)
        {
            var paidEvent = new CorporateAtmTaxPaidEvent(quote);
            RaiseLocalEvent(ref paidEvent);
            return;
        }

        foreach (var (account, taxCoeff) in atm.Comp.TaxAccounts)
        {
            var amount = GetAtmDepositFee(deposit, taxCoeff);
            if (amount > 0)
                TrySectorDeposit(account, amount, LedgerEntryType.AtmTax);
        }
    }
}
