using Content.Server._Exodus.Territory;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Territory;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Bank.Components;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.CorporateIncome;

public sealed partial class CorporateIncomeSystem : EntitySystem
{
    private static readonly ProtoId<CorporateIncomePrototype> ConfigId = "Default";

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<GridTerritoryCorporateControllerChangedEvent>(OnCorporateControllerChanged);
        InitializeAtmTaxes();
        InitializePayments();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!TryGetLedger(out var ledger))
            return;

        var now = _timing.CurTime;
        var treasuryDue = now >= ledger.Comp.NextTreasuryPayout;
        var salaryDue = now >= ledger.Comp.NextSalaryPayout;
        if ((!treasuryDue && !salaryDue) || !TryGetConfig(out var config))
            return;

        foreach (var (company, account) in ledger.Comp.Accounts)
        {
            Accrue(account, config, now);
            if (config.BankAccounts.TryGetValue(company, out var bankAccount))
            {
                if (treasuryDue)
                    PayTreasury(account, bankAccount);
            }
            else if (salaryDue)
            {
                PaySalary(company, account);
            }
        }

        if (treasuryDue)
        {
            PaySectorTaxes(ledger);
            ledger.Comp.NextTreasuryPayout = NextBoundary(ledger.Comp.NextTreasuryPayout, config.TreasuryInterval, now);
        }

        if (salaryDue)
            ledger.Comp.NextSalaryPayout = NextBoundary(ledger.Comp.NextSalaryPayout, config.SalaryInterval, now);
    }

    private void OnRoundStarted(RoundStartedEvent args)
    {
        if (TryGetLedger(out _))
            return;

        if (!TryGetConfig(out var config))
        {
            Log.Error("Missing or invalid corporate income configuration.");
            return;
        }

        var now = _timing.CurTime;
        var uid = Spawn(null, MapCoordinates.Nullspace);
        var component = AddComp<CorporateIncomeLedgerComponent>(uid);
        component.Active = true;
        component.NextTreasuryPayout = now + config.TreasuryInterval;
        component.NextSalaryPayout = now + config.SalaryInterval;
        var ledger = new Entity<CorporateIncomeLedgerComponent>(uid, component);

        var query = EntityQueryEnumerator<GridTerritoryComponent>();
        while (query.MoveNext(out var grid, out var territory))
        {
            UpdateClaim(ledger, (grid, territory), territory.CorporateController, config, now);
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var query = AllEntityQuery<CorporateIncomeLedgerComponent>();
        while (query.MoveNext(out var uid, out var ledger))
        {
            ledger.Active = false;
            QueueDel(uid);
        }
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        if (args.New == GameRunLevel.InRound || !TryGetLedger(out var ledger))
            return;

        if (TryGetConfig(out var config))
        {
            foreach (var (company, account) in ledger.Comp.Accounts)
            {
                Accrue(account, config, _timing.CurTime);
                if (config.BankAccounts.TryGetValue(company, out var bankAccount))
                    PayTreasury(account, bankAccount);
            }

            PaySectorTaxes(ledger);
        }

        ledger.Comp.Active = false;
    }

    private void OnCorporateControllerChanged(ref GridTerritoryCorporateControllerChangedEvent args)
    {
        if (!TryGetLedger(out var ledger) || !TryGetConfig(out var config))
            return;

        // A shutdown notification must release the claim even if its component is no longer available.
        if (args.NewCompany is null)
        {
            RemoveClaim(ledger, args.Grid, config, _timing.CurTime);
            return;
        }

        if (TryComp<GridTerritoryComponent>(args.Grid, out var territory))
            UpdateClaim(ledger, (args.Grid, territory), args.NewCompany, config, _timing.CurTime);
    }

    private void UpdateClaim(
        Entity<CorporateIncomeLedgerComponent> ledger,
        Entity<GridTerritoryComponent> territory,
        ProtoId<CompanyPrototype>? company,
        CorporateIncomePrototype config,
        TimeSpan now)
    {
        RemoveClaim(ledger, territory.Owner, config, now);
        if (company is not { } controller || !territory.Comp.Claimable ||
            TerminatingOrDeleted(territory.Owner) || config.ExcludedCompanies.Contains(controller) ||
            !_prototypes.HasIndex(controller))
        {
            return;
        }

        var points = TerritoryCounterSystem.GetPoints(territory.Comp.Radius);
        if (points <= 0)
            return;

        var account = GetAccount(ledger, controller, now);
        Accrue(account, config, now);
        account.Points += points;
        ledger.Comp.Claims.Add(territory.Owner, new CorporateIncomeClaim(controller, points));
    }

    private static void RemoveClaim(
        Entity<CorporateIncomeLedgerComponent> ledger,
        EntityUid grid,
        CorporateIncomePrototype config,
        TimeSpan now)
    {
        if (!ledger.Comp.Claims.Remove(grid, out var claim))
            return;

        var account = ledger.Comp.Accounts[claim.Company];
        Accrue(account, config, now);
        account.Points -= claim.Points;
    }

    private static CorporateIncomeAccount GetAccount(
        Entity<CorporateIncomeLedgerComponent> ledger,
        ProtoId<CompanyPrototype> company,
        TimeSpan now)
    {
        if (ledger.Comp.Accounts.TryGetValue(company, out var account))
            return account;

        account = new CorporateIncomeAccount { LastAccrual = now };
        ledger.Comp.Accounts.Add(company, account);
        return account;
    }

    internal static void Accrue(CorporateIncomeAccount account, CorporateIncomePrototype config, TimeSpan now)
    {
        if (now <= account.LastAccrual || config.IncomeInterval <= TimeSpan.Zero)
            return;

        var elapsed = now - account.LastAccrual;
        // Keep an integer numerator so repeated settlements neither lose fractions nor overflow a TimeSpan.
        var income = account.AccrualRemainder + (decimal) elapsed.Ticks * account.Points * config.IncomePerPoint;
        account.PassiveIncome += decimal.Floor(income / config.IncomeInterval.Ticks);
        account.AccrualRemainder = income % config.IncomeInterval.Ticks;
        account.LastAccrual = now;
    }

    private static TimeSpan NextBoundary(TimeSpan next, TimeSpan interval, TimeSpan now)
    {
        var elapsedIntervals = (now - next).Ticks / interval.Ticks + 1;
        return next + TimeSpan.FromTicks(elapsedIntervals * interval.Ticks);
    }

    private bool TryGetLedger(out Entity<CorporateIncomeLedgerComponent> ledger)
    {
        var query = EntityQueryEnumerator<CorporateIncomeLedgerComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.Active || TerminatingOrDeleted(uid))
                continue;

            ledger = (uid, component);
            return true;
        }

        ledger = default;
        return false;
    }

    private bool TryGetConfig(out CorporateIncomePrototype config)
    {
        if (!_prototypes.TryIndex(ConfigId, out var prototype) ||
            prototype.IncomePerPoint < 0 || prototype.IncomeInterval <= TimeSpan.Zero ||
            prototype.TreasuryInterval <= TimeSpan.Zero || prototype.SalaryInterval <= TimeSpan.Zero ||
            !float.IsFinite(prototype.AtmCompanyRate) || prototype.AtmCompanyRate < 0f)
        {
            config = default!;
            return false;
        }

        var totalRate = (double) prototype.AtmCompanyRate;
        foreach (var (account, rate) in prototype.AtmTaxAccounts)
        {
            if (account == SectorBankAccount.Invalid || !float.IsFinite(rate) || rate < 0f)
            {
                config = default!;
                return false;
            }

            totalRate += rate;
        }

        if (totalRate > 1d)
        {
            config = default!;
            return false;
        }

        foreach (var destination in prototype.BankAccounts.Values)
        {
            if (destination != SectorBankAccount.Invalid)
                continue;

            config = default!;
            return false;
        }

        config = prototype;
        return true;
    }
}
