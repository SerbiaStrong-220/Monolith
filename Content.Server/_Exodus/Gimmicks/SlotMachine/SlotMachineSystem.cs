using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Stack;
using Content.Shared._Exodus.Gimmicks.SlotMachine;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Power;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Gimmicks.SlotMachine;

public sealed class SlotMachineSystem : EntitySystem
{
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly StackSystem _stack = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ChatSystem _chat = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private static readonly TimeSpan SpinDuration = TimeSpan.FromSeconds(2.5);
    private static readonly TimeSpan CollectionFailSafeDelay = TimeSpan.FromSeconds(1);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SlotMachineComponent, AfterActivatableUIOpenEvent>(OnAfterUiOpen);
        SubscribeLocalEvent<SlotMachineComponent, PowerChangedEvent>(OnPowerChanged);
        SubscribeLocalEvent<SlotMachineComponent, SlotMachineCollectDoAfterEvent>(OnCollectDoAfter);

        Subs.BuiEvents<SlotMachineComponent>(SlotMachineUiKey.Key, subs =>
        {
            subs.Event<SlotMachineSpinMessage>(OnSpin);
            subs.Event<SlotMachineInsertMessage>(OnInsert);
            subs.Event<SlotMachineCollectMessage>(OnCollect);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SlotMachineComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (comp.HasPendingCollection && now > comp.CollectionEndTime + CollectionFailSafeDelay)
            {
                comp.HasPendingCollection = false;
                comp.CollectionEndTime = TimeSpan.Zero;
                UpdateUi((uid, comp));
            }
            if (!comp.HasPendingResult || now < comp.SpinEndTime)
                continue;

            comp.HasPendingResult = false;
            comp.Reels = comp.PendingReels;
            comp.IsWin = comp.PendingIsWin;
            comp.WinText = comp.PendingWinText;
            comp.LastPayout = comp.PendingPayout;
            comp.StoredCredits = (int) Math.Min(int.MaxValue, (long) comp.StoredCredits + comp.PendingPayout);
            Dirty(uid, comp);

            if (comp.IsWin)
                _audio.PlayPvs(comp.WinSound, uid);

            if (comp.PendingJackpotWinner is { } winner && !Deleted(winner))
                AnnounceJackpot(winner, comp.LastPayout);

            comp.PendingJackpotWinner = null;
            UpdateUi((uid, comp));
        }
    }

    private void OnAfterUiOpen(Entity<SlotMachineComponent> entity, ref AfterActivatableUIOpenEvent args)
    {
        UpdateUi(entity);
    }

    private void OnPowerChanged(Entity<SlotMachineComponent> entity, ref PowerChangedEvent args)
    {
        if (IsPowered(entity.Owner))
            return;

        _ui.CloseUi(entity.Owner, SlotMachineUiKey.Key);
    }

    private void OnInsert(Entity<SlotMachineComponent> entity, ref SlotMachineInsertMessage args)
    {
        if (!EnsureAvailable(entity, args.Actor))
            return;

        if (args.Amount < SlotMachineComponent.MinInsert)
            return;

        if (!_hands.TryGetActiveItem(args.Actor, out var item) ||
            !TryComp<StackComponent>(item, out var stack) ||
            stack.StackTypeId != SlotMachineComponent.CreditStackId)
        {
            _popup.PopupEntity(Loc.GetString("slot-machine-popup-no-credits"), args.Actor, args.Actor);
            return;
        }

        var amountToTake = Math.Min(args.Amount, stack.Count);
        if (amountToTake < SlotMachineComponent.MinInsert)
        {
            _popup.PopupEntity(Loc.GetString("slot-machine-popup-no-credits"), args.Actor, args.Actor);
            return;
        }

        if (!_stack.Use(item.Value, amountToTake, stack))
            return;

        entity.Comp.StoredCredits = (int) Math.Min(int.MaxValue, (long) entity.Comp.StoredCredits + amountToTake);
        Dirty(entity);
        _audio.PlayPvs(entity.Comp.InsertSound, entity.Owner);
        _popup.PopupEntity(Loc.GetString("slot-machine-popup-inserted", ("amount", amountToTake)), args.Actor, args.Actor);
        UpdateUi(entity);
    }

    private void OnSpin(Entity<SlotMachineComponent> entity, ref SlotMachineSpinMessage args)
    {
        if (!EnsureAvailable(entity, args.Actor))
            return;

        var bet = Math.Max(SlotMachineComponent.MinBet, args.Bet);
        if (entity.Comp.StoredCredits < bet)
        {
            _popup.PopupEntity(Loc.GetString("slot-machine-popup-no-funds"), args.Actor, args.Actor);
            return;
        }

        if (!TryBuildReels(entity.Comp, out var reels))
        {
            _popup.PopupEntity(Loc.GetString("slot-machine-popup-unavailable"), args.Actor, args.Actor);
            return;
        }

        entity.Comp.StoredCredits -= bet;
        entity.Comp.LastBet = bet;
        var (isWin, winText, payout) = CalculateResult(entity.Comp, reels, bet);
        payout = Math.Min(payout, int.MaxValue - entity.Comp.StoredCredits);

        entity.Comp.PendingReels = reels;
        entity.Comp.PendingIsWin = isWin;
        entity.Comp.PendingWinText = winText;
        entity.Comp.PendingPayout = payout;
        entity.Comp.PendingJackpotWinner = IsDiamondJackpot(reels) ? args.Actor : null;
        entity.Comp.HasPendingResult = true;
        entity.Comp.SpinEndTime = _timing.CurTime + SpinDuration;

        Dirty(entity);
        _audio.PlayPvs(entity.Comp.SpinSound, entity.Owner);
        UpdateUi(entity);
    }

    private void OnCollect(Entity<SlotMachineComponent> entity, ref SlotMachineCollectMessage args)
    {
        if (!EnsureAvailable(entity, args.Actor) || entity.Comp.StoredCredits <= 0)
            return;

        var doAfter = new DoAfterArgs(EntityManager, args.Actor, entity.Comp.CollectDuration,
            new SlotMachineCollectDoAfterEvent(), entity.Owner, target: entity.Owner, used: entity.Owner)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false,
        };

        entity.Comp.HasPendingCollection = true;
        entity.Comp.CollectionEndTime = _timing.CurTime + entity.Comp.CollectDuration;
        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            entity.Comp.HasPendingCollection = false;
            entity.Comp.CollectionEndTime = TimeSpan.Zero;
            return;
        }

        UpdateUi(entity);
    }

    private void OnCollectDoAfter(Entity<SlotMachineComponent> entity, ref SlotMachineCollectDoAfterEvent args)
    {
        if (!entity.Comp.HasPendingCollection)
        {
            args.Handled = true;
            return;
        }

        entity.Comp.HasPendingCollection = false;
        entity.Comp.CollectionEndTime = TimeSpan.Zero;
        if (args.Cancelled || args.Handled)
        {
            UpdateUi(entity);
            return;
        }

        if (!IsPowered(entity.Owner))
        {
            _popup.PopupEntity(Loc.GetString("slot-machine-popup-no-power"), args.User, args.User);
            UpdateUi(entity);
            return;
        }

        TryCollectCredits(entity, args.User);
        args.Handled = true;
        UpdateUi(entity);
    }

    private bool TryCollectCredits(Entity<SlotMachineComponent> entity, EntityUid actor)
    {
        if (entity.Comp.StoredCredits <= 0)
            return false;

        var credits = entity.Comp.StoredCredits;
        var money = Spawn(SlotMachineComponent.CashPrototypeId, Transform(entity.Owner).Coordinates);
        if (!TryComp<StackComponent>(money, out var stack))
        {
            QueueDel(money);
            return false;
        }

        _stack.SetCount(money, credits, stack);
        entity.Comp.StoredCredits = 0;
        Dirty(entity);
        _popup.PopupEntity(Loc.GetString("slot-machine-popup-collected", ("amount", credits)), actor, actor);
        return true;
    }

    private bool EnsureAvailable(Entity<SlotMachineComponent> entity, EntityUid actor)
    {
        if (entity.Comp.HasPendingCollection)
        {
            _popup.PopupEntity(Loc.GetString("slot-machine-popup-collecting"), actor, actor);
            return false;
        }

        if (entity.Comp.HasPendingResult)
        {
            _popup.PopupEntity(Loc.GetString("slot-machine-popup-spinning"), actor, actor);
            return false;
        }

        if (IsPowered(entity.Owner))
            return true;

        _popup.PopupEntity(Loc.GetString("slot-machine-popup-no-power"), actor, actor);
        return false;
    }

    private bool IsPowered(EntityUid uid)
    {
        return !TryComp<ApcPowerReceiverComponent>(uid, out var power) || power.Powered;
    }

    private bool TryBuildReels(SlotMachineComponent component, out List<string> reels)
    {
        reels = new List<string>(component.ReelPools.Count);
        foreach (var pool in component.ReelPools)
        {
            if (!TryPickSymbol(pool, out var symbol))
            {
                reels.Clear();
                return false;
            }

            reels.Add(symbol);
        }

        return reels.Count > 0;
    }

    private bool TryPickSymbol(SlotMachineReelDef pool, out string symbolId)
    {
        var totalWeight = 0f;
        SlotMachineSymbolDef? lastSymbol = null;
        foreach (var symbol in pool.Symbols)
        {
            if (!float.IsFinite(symbol.Weight) || symbol.Weight <= 0f)
                continue;

            totalWeight += symbol.Weight;
            lastSymbol = symbol;
        }

        if (lastSymbol == null || totalWeight <= 0f || !float.IsFinite(totalWeight))
        {
            symbolId = string.Empty;
            return false;
        }

        var roll = _random.NextFloat(totalWeight);
        foreach (var symbol in pool.Symbols)
        {
            if (!float.IsFinite(symbol.Weight) || symbol.Weight <= 0f)
                continue;

            roll -= symbol.Weight;
            if (roll <= 0f)
            {
                symbolId = symbol.Id;
                return true;
            }
        }

        symbolId = lastSymbol.Id;
        return true;
    }

    private static (bool IsWin, string WinText, int Payout) CalculateResult(SlotMachineComponent component, List<string> reels, int bet)
    {
        foreach (var rule in component.Rules)
        {
            if (rule.Symbols.Count == 0 || rule.Multiplier <= 0 || !Matches(rule, reels))
                continue;

            var payout = (int) Math.Min(int.MaxValue, (long) bet * rule.Multiplier);
            return (true, rule.WinText, payout);
        }

        return (false, string.Empty, 0);
    }

    private static bool Matches(SlotMachineRule rule, List<string> reels)
    {
        var startIndex = rule.Index ?? 0;
        if (startIndex < 0 || startIndex + rule.Symbols.Count > reels.Count)
            return false;

        if (!rule.Index.HasValue && rule.Symbols.Count != reels.Count)
            return false;

        for (var i = 0; i < rule.Symbols.Count; i++)
        {
            if (reels[startIndex + i] != rule.Symbols[i])
                return false;
        }

        return true;
    }

    private static bool IsDiamondJackpot(List<string> reels)
    {
        return reels.Count == 3 &&
               reels[0] == "diamond" &&
               reels[1] == "diamond" &&
               reels[2] == "diamond";
    }

    private void AnnounceJackpot(EntityUid winner, int payout)
    {
        var players = GetPlayersOnMap(winner);
        var message = Loc.GetString("slot-machine-announcement-jackpot",
            ("winner", Identity.Name(winner, EntityManager)),
            ("amount", payout));

        _chat.DispatchFilteredAnnouncement(players, message,
            sender: Loc.GetString("slot-machine-announcement-sender"));
    }

    private Filter GetPlayersOnMap(EntityUid source)
    {
        var players = Filter.Empty();
        var mapId = Transform(source).MapID;
        foreach (var session in Filter.GetAllPlayers(_playerManager))
        {
            if (session.AttachedEntity is not { } player || Deleted(player) || Transform(player).MapID != mapId)
                continue;

            players.AddPlayer(session);
        }

        return players;
    }

    private void UpdateUi(Entity<SlotMachineComponent> entity)
    {
        _ui.SetUiState(entity.Owner,
            SlotMachineUiKey.Key,
            new SlotMachineBoundUserInterfaceState(
                entity.Comp.Reels,
                entity.Comp.StoredCredits,
                entity.Comp.IsWin,
                entity.Comp.WinText,
                entity.Comp.LastBet,
                entity.Comp.LastPayout,
                entity.Comp.HasPendingResult,
                entity.Comp.HasPendingCollection,
                entity.Comp.Rules,
                entity.Comp.ReelPools));
    }
}
