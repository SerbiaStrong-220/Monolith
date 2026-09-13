using Content.Server._NF.Bank;
using Content.Server.Chat.Managers;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Bank.BUI;
using Content.Shared._NF.Bank.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.CorporateIncome;

public sealed partial class CorporateIncomeSystem
{
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private ISharedPlayerManager _players = default!;

    private EntityQuery<CompanyComponent> _companyQuery;
    private EntityQuery<BankAccountComponent> _bankQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<MetaDataComponent> _metadataQuery;

    private void InitializePayments()
    {
        _companyQuery = GetEntityQuery<CompanyComponent>();
        _bankQuery = GetEntityQuery<BankAccountComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _metadataQuery = GetEntityQuery<MetaDataComponent>();
    }

    private void PayTreasury(CorporateIncomeAccount account, SectorBankAccount destination)
    {
        account.PassiveIncome -= DepositSector(destination, account.PassiveIncome, LedgerEntryType.CorporateTerritoryIncome);
        account.AtmIncome -= DepositSector(destination, account.AtmIncome, LedgerEntryType.AtmTax);
    }

    private void PaySectorTaxes(Entity<CorporateIncomeLedgerComponent> ledger)
    {
        foreach (var (destination, amount) in ledger.Comp.PendingSectorTaxes)
        {
            var paid = DepositSector(destination, amount, LedgerEntryType.AtmTax);
            ledger.Comp.PendingSectorTaxes[destination] -= paid;
        }
    }

    private int DepositSector(SectorBankAccount destination, decimal available, LedgerEntryType reason)
    {
        if (available < 1m || !_bank.TryGetBalance(destination, out var balance))
            return 0;

        var capacity = Math.Min((decimal) int.MaxValue, (decimal) int.MaxValue - balance);
        var amount = (int) Math.Min(decimal.Floor(available), capacity);
        if (amount <= 0 || !_bank.TrySectorDeposit(destination, amount, reason))
            return 0;

        return amount;
    }

    private void PaySalary(ProtoId<CompanyPrototype> company, CorporateIncomeAccount account)
    {
        var available = decimal.Floor(account.PassiveIncome) + account.AtmIncome;
        if (available < 1m)
            return;

        // This list is allocated only for a funded company's hourly payout, never during ordinary update ticks.
        var recipients = new List<(ICommonSession Session, Entity<BankAccountComponent> Bank)>();
        foreach (var session in _players.Sessions)
        {
            if (TryGetSalaryRecipient(session, company, out var recipient))
                recipients.Add((session, recipient));
        }

        if (recipients.Count == 0)
            return;

        var salary = (int) Math.Min(decimal.Floor(available / recipients.Count), int.MaxValue);
        if (salary <= 0 || !_prototypes.TryIndex(company, out var prototype))
            return;

        foreach (var (session, recipient) in recipients)
        {
            if (!TryGetSalaryRecipient(session, company, out var current) || current.Owner != recipient.Owner ||
                (long) current.Comp.Balance + salary > int.MaxValue ||
                !_bank.TryBankDeposit(current.Owner, salary, tax: false))
            {
                continue;
            }

            var fromAtm = Math.Min(account.AtmIncome, salary);
            account.AtmIncome -= fromAtm;
            account.PassiveIncome -= salary - fromAtm;
            _chat.DispatchServerMessage(session, Loc.GetString("corporate-income-salary",
                ("company", Loc.GetString(prototype.Name)),
                ("amount", BankSystemExtensions.ToSpesoString(salary))));
        }
    }

    private bool TryGetSalaryRecipient(
        ICommonSession session,
        ProtoId<CompanyPrototype> company,
        out Entity<BankAccountComponent> recipient)
    {
        recipient = default;
        if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } uid ||
            !_metadataQuery.TryGetComponent(uid, out var metadata) || !metadata.EntityInitialized ||
            metadata.EntityLifeStage >= EntityLifeStage.Terminating ||
            !_companyQuery.TryGetComponent(uid, out var member) || member.CompanyName != company ||
            !_mobQuery.TryGetComponent(uid, out var mob) || mob.CurrentState == MobState.Dead ||
            !_bankQuery.TryGetComponent(uid, out var bank))
        {
            return false;
        }

        recipient = (uid, bank);
        return true;
    }
}
