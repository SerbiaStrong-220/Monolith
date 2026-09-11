using Content.Server.Administration.Logs;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.Stack;
using Content.Shared._Exodus.TerritoryIncome;
using Content.Shared.Database;
using Content.Shared.NPC.Systems;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server._Exodus.TerritoryIncome;

public sealed partial class TerritoryIncomeSystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private NpcFactionSystem _factions = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private void InitializeTerminals()
    {
        SubscribeLocalEvent<TerritoryIncomeTerminalComponent, ActivatableUIOpenAttemptEvent>(OnOpenAttempt);
        Subs.BuiEvents<TerritoryIncomeTerminalComponent>(TerritoryIncomeUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<TerritoryIncomeWithdrawMessage>(OnWithdraw);
        });
    }

    private void OnOpenAttempt(Entity<TerritoryIncomeTerminalComponent> ent, ref ActivatableUIOpenAttemptEvent args)
    {
        if (_prototypes.TryIndex(ent.Comp.Account, out var config) && _factions.IsMember(args.User, config.AccessFaction))
            return;

        args.Cancel();
        _popup.PopupEntity(Loc.GetString("territory-income-access-denied"), ent.Owner, args.User);
    }

    private void OnUiOpened(Entity<TerritoryIncomeTerminalComponent> ent, ref BoundUIOpenedEvent args)
    {
        // Check again here: opening a BUI directly must not bypass the activatable UI check.
        if (!_prototypes.TryIndex(ent.Comp.Account, out var config) || !_factions.IsMember(args.Actor, config.AccessFaction))
        {
            _ui.CloseUi(ent.Owner, TerritoryIncomeUiKey.Key, args.Actor);
            return;
        }

        if (!TryGetAccount(ent.Comp.Account, out var account))
        {
            _popup.PopupEntity(Loc.GetString("territory-income-unavailable"), ent.Owner, args.Actor);
            _ui.CloseUi(ent.Owner, TerritoryIncomeUiKey.Key, args.Actor);
            return;
        }

        Settle(account, config, _timing.CurTime);
        RefreshTerminals(account, config);
    }

    private void OnWithdraw(Entity<TerritoryIncomeTerminalComponent> ent, ref TerritoryIncomeWithdrawMessage args)
    {
        var actor = args.Actor;
        if (TerminatingOrDeleted(actor) || !MetaData(actor).EntityInitialized ||
            !_ui.IsUiOpen(ent.Owner, TerritoryIncomeUiKey.Key, actor) ||
            !_prototypes.TryIndex(ent.Comp.Account, out var config) || !_factions.IsMember(actor, config.AccessFaction) ||
            !TryGetAccount(ent.Comp.Account, out var account) || !account.Comp.Active)
        {
            return;
        }

        Settle(account, config, _timing.CurTime);
        var amount = args.Amount;
        if (amount <= 0 || amount > config.MaxWithdrawal || amount > account.Comp.Balance)
        {
            _popup.PopupEntity(Loc.GetString("territory-income-invalid-amount"), ent.Owner, actor);
            RefreshTerminals(account, config);
            return;
        }

        if (!_prototypes.TryIndex(config.CurrencyStack, out var cashPrototype) || amount > (cashPrototype.MaxCount ?? int.MaxValue))
            return;

        // Reserve from the single ledger before spawning anything, including for simultaneous terminals.
        account.Comp.Balance -= amount;
        EntityUid? cash = null;
        var completed = false;
        try
        {
            cash = Spawn(cashPrototype.Spawn, Transform(actor).Coordinates);
            if (TryComp<StackComponent>(cash, out var stack) && stack.StackTypeId == config.CurrencyStack.Id &&
                amount <= _stack.GetMaxCount(stack))
            {
                _stack.SetCount(cash.Value, amount, stack);
                completed = stack.Count == amount;
            }
        }
        catch (Exception exception)
        {
            Log.Error($"Could not withdraw from territory income account {config.ID}: {exception}");
        }
        finally
        {
            if (!completed)
            {
                account.Comp.Balance += amount;
                if (cash is { } failedCash && !Deleted(failedCash))
                    QueueDel(failedCash);
            }

            RefreshTerminals(account, config);
        }

        if (!completed || cash is not { } withdrawnCash)
        {
            _popup.PopupEntity(Loc.GetString("territory-income-unavailable"), ent.Owner, actor);
            return;
        }

        _hands.PickupOrDrop(actor, withdrawnCash);
        _adminLog.Add(LogType.StorePurchase, LogImpact.Medium,
            $"{ToPrettyString(actor):player} withdrew {amount} units of {config.CurrencyStack} from territory income account {config.ID} using {ToPrettyString(ent.Owner)}.");
    }

    private void RefreshTerminals(Entity<TerritoryIncomeAccountComponent> account, TerritoryIncomePrototype config)
    {
        TerritoryIncomeUiState? state = null;
        var query = EntityQueryEnumerator<TerritoryIncomeTerminalComponent>();
        while (query.MoveNext(out var uid, out var terminal))
        {
            if (terminal.Account != account.Comp.Account || !_ui.IsUiOpen(uid, TerritoryIncomeUiKey.Key))
                continue;

            state ??= BuildState(account, config);
            _ui.SetUiState(uid, TerritoryIncomeUiKey.Key, state);
        }
    }

    private TerritoryIncomeUiState BuildState(Entity<TerritoryIncomeAccountComponent> account, TerritoryIncomePrototype config)
    {
        var territories = new List<TerritoryIncomeEntry>(account.Comp.Territories.Count);
        foreach (var (grid, points) in account.Comp.Territories)
        {
            if (!TerminatingOrDeleted(grid))
                territories.Add(new TerritoryIncomeEntry(Name(grid), points));
        }

        territories.Sort((left, right) => string.Compare(left.Name, right.Name, StringComparison.Ordinal));
        var maxWithdrawal = config.MaxWithdrawal;
        if (_prototypes.TryIndex(config.CurrencyStack, out var stack))
            maxWithdrawal = Math.Min(maxWithdrawal, stack.MaxCount ?? int.MaxValue);

        return new TerritoryIncomeUiState(config.Name, config.CurrencyName, account.Comp.Balance, account.Comp.Points,
            config.CurrencyPerPoint, maxWithdrawal, config.PayoutInterval, account.Comp.NextPayout, account.Comp.Active, territories);
    }
}
