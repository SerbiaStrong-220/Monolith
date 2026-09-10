using Content.Server.GameTicking;
using Content.Server._Exodus.Territory;
using Content.Shared._Exodus.Territory;
using Content.Shared._Exodus.TerritoryIncome;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.TerritoryIncome;

public sealed partial class TerritoryIncomeSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnRunLevelChanged);
        SubscribeLocalEvent<GridTerritoryControllerChangedEvent>(OnTerritoryChanged);
        InitializeTerminals();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<TerritoryIncomeAccountComponent>();
        while (query.MoveNext(out var uid, out var account))
        {
            if (!account.Active || now < account.NextPayout)
                continue;

            if (!_prototypes.TryIndex(account.Account, out var config))
            {
                account.Active = false;
                continue;
            }

            Settle((uid, account), config, now);
            RefreshTerminals((uid, account), config);
        }
    }

    private void OnRoundStarted(RoundStartedEvent args)
    {
        var now = _timing.CurTime;
        foreach (var config in _prototypes.EnumeratePrototypes<TerritoryIncomePrototype>())
        {
            if (config.PayoutInterval <= TimeSpan.Zero || config.CurrencyPerPoint <= 0 || config.MaxWithdrawal <= 0)
            {
                Log.Error($"Invalid territory income configuration: {config.ID}.");
                continue;
            }

            var uid = Spawn(null, MapCoordinates.Nullspace);
            var account = AddComp<TerritoryIncomeAccountComponent>(uid);
            account.Account = config.ID;
            account.Active = true;
            account.LastAccrual = now;
            account.NextPayout = now + config.PayoutInterval;

            var query = EntityQueryEnumerator<GridTerritoryComponent>();
            while (query.MoveNext(out var grid, out var territory))
            {
                if (config.ExcludeInitialClaims && territory.ControllingFaction != null)
                    account.InitialClaims.Add(grid);

                UpdateTerritory((uid, account), config, (grid, territory), territory.ControllingFaction);
            }

            RefreshTerminals((uid, account), config);
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        // Ledgers are in nullspace and need explicit cleanup along with the round.
        var query = AllEntityQuery<TerritoryIncomeAccountComponent>();
        while (query.MoveNext(out var uid, out var account))
        {
            account.Active = false;
            QueueDel(uid);
        }
    }

    private void OnRunLevelChanged(GameRunLevelChangedEvent args)
    {
        if (args.New == GameRunLevel.InRound)
            return;

        var query = EntityQueryEnumerator<TerritoryIncomeAccountComponent>();
        while (query.MoveNext(out var uid, out var account))
        {
            account.Active = false;
            if (_prototypes.TryIndex(account.Account, out var config))
                RefreshTerminals((uid, account), config);
        }
    }

    private void OnTerritoryChanged(ref GridTerritoryControllerChangedEvent args)
    {
        // Shutdown also releases a claim, so process it while the grid component still exists.
        if (!TryComp<GridTerritoryComponent>(args.Grid, out var territory))
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<TerritoryIncomeAccountComponent>();
        while (query.MoveNext(out var uid, out var account))
        {
            if (!account.Active || !_prototypes.TryIndex(account.Account, out var config) ||
                args.OldFaction != config.Faction && args.NewFaction != config.Faction)
            {
                continue;
            }

            // Finish the old rate before changing the score, including any elapsed payout boundaries.
            Settle((uid, account), config, now);
            UpdateTerritory((uid, account), config, (args.Grid, territory), args.NewFaction);
            RefreshTerminals((uid, account), config);
        }
    }

    private void UpdateTerritory(
        Entity<TerritoryIncomeAccountComponent> account,
        TerritoryIncomePrototype config,
        Entity<GridTerritoryComponent> territory,
        ProtoId<TerritoryFactionPrototype>? controller)
    {
        if (account.Comp.Territories.Remove(territory.Owner, out var previousPoints))
            account.Comp.Points -= previousPoints;

        if (controller != config.Faction || !territory.Comp.Claimable || account.Comp.InitialClaims.Contains(territory.Owner))
            return;

        var points = TerritoryCounterSystem.GetPoints(territory.Comp.Radius);
        if (points <= 0)
            return;

        account.Comp.Territories.Add(territory.Owner, points);
        account.Comp.Points += points;
    }

    private void Settle(Entity<TerritoryIncomeAccountComponent> account, TerritoryIncomePrototype config, TimeSpan now)
    {
        if (!account.Comp.Active || config.PayoutInterval <= TimeSpan.Zero)
            return;

        var intervalTicks = config.PayoutInterval.Ticks;
        if (now >= account.Comp.NextPayout)
        {
            var elapsedIntervals = (now - account.Comp.NextPayout).Ticks / intervalTicks;
            var lastBoundary = account.Comp.NextPayout + TimeSpan.FromTicks(elapsedIntervals * intervalTicks);
            Accrue(account, config, lastBoundary);

            var earned = account.Comp.PendingIncomeTime.Ticks / intervalTicks;
            account.Comp.Balance += (int) Math.Min(earned, int.MaxValue - account.Comp.Balance);
            account.Comp.PendingIncomeTime = TimeSpan.FromTicks(account.Comp.PendingIncomeTime.Ticks % intervalTicks);
            account.Comp.NextPayout += TimeSpan.FromTicks((elapsedIntervals + 1) * intervalTicks);
        }

        Accrue(account, config, now);
    }

    private static void Accrue(Entity<TerritoryIncomeAccountComponent> account, TerritoryIncomePrototype config, TimeSpan until)
    {
        if (until <= account.Comp.LastAccrual)
            return;

        var elapsed = until - account.Comp.LastAccrual;
        account.Comp.PendingIncomeTime += TimeSpan.FromTicks(elapsed.Ticks * account.Comp.Points * config.CurrencyPerPoint);
        account.Comp.LastAccrual = until;
    }

    private bool TryGetAccount(ProtoId<TerritoryIncomePrototype> id, out Entity<TerritoryIncomeAccountComponent> account)
    {
        var query = EntityQueryEnumerator<TerritoryIncomeAccountComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Account != id || TerminatingOrDeleted(uid))
                continue;

            account = (uid, component);
            return true;
        }

        account = default;
        return false;
    }
}
