using Content.Server._NF.Bank;
using Content.Shared._Exodus.Territory;
using Content.Shared._NF.Bank.Components;

namespace Content.Server._Exodus.CorporateIncome;

public sealed partial class CorporateIncomeSystem
{
    private void InitializeAtmTaxes()
    {
        SubscribeLocalEvent<BankATMComponent, GetCorporateAtmTaxEvent>(OnGetAtmTax);
        SubscribeLocalEvent<CorporateAtmTaxPaidEvent>(OnAtmTaxPaid);
    }

    private void OnGetAtmTax(Entity<BankATMComponent> ent, ref GetCorporateAtmTaxEvent args)
    {
        if (args.Quote != null || args.Deposit <= 0 || !TryGetLedger(out var ledger) ||
            Transform(ent).GridUid is not { } grid ||
            !TryComp<GridTerritoryComponent>(grid, out var territory) || !territory.Claimable ||
            territory.CorporateController is not { } company ||
            !TryGetConfig(out var config) || config.ExcludedCompanies.Contains(company) ||
            !_prototypes.HasIndex(company))
        {
            return;
        }

        var companyAmount = BankSystem.GetAtmDepositFee(args.Deposit, config.AtmCompanyRate);
        var sectorAmounts = new Dictionary<SectorBankAccount, int>();
        var total = (long) companyAmount;
        foreach (var (account, rate) in config.AtmTaxAccounts)
        {
            var amount = BankSystem.GetAtmDepositFee(args.Deposit, rate);
            sectorAmounts.Add(account, amount);
            total += amount;
        }

        if (total > args.Deposit)
            return;

        args.Quote = new CorporateAtmTaxQuote(ledger.Owner, company, companyAmount, sectorAmounts, (int) total);
    }

    private void OnAtmTaxPaid(ref CorporateAtmTaxPaidEvent args)
    {
        var quote = args.Quote;
        if (TerminatingOrDeleted(quote.Ledger) ||
            !TryComp<CorporateIncomeLedgerComponent>(quote.Ledger, out var component))
        {
            Log.Error($"Corporate ATM deposit lost its ledger for {quote.Company}.");
            return;
        }

        var ledger = new Entity<CorporateIncomeLedgerComponent>(quote.Ledger, component);
        var account = GetAccount(ledger, quote.Company, _timing.CurTime);
        account.AtmIncome += quote.CompanyAmount;
        foreach (var (destination, amount) in quote.SectorAmounts)
        {
            component.PendingSectorTaxes.TryGetValue(destination, out var pending);
            component.PendingSectorTaxes[destination] = pending + amount;
        }

        if (TryGetConfig(out var config) && config.BankAccounts.TryGetValue(quote.Company, out var bankAccount))
            PayTreasury(account, bankAccount);

        PaySectorTaxes(ledger);
    }
}
