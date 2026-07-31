using Content.Server._NF.Bank;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Shared._Exodus.Casino;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.IdentityManagement;
using Content.Shared.Preferences;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Casino;

public sealed partial class RouletteSystem : EntitySystem
{
    private const float SpinVolume = -2f;
    private const float SpinFadeDuration = 3f;
    private static readonly TimeSpan SettlementRetryDelay = TimeSpan.FromSeconds(5);

    private static readonly HashSet<int> RedNumbers =
    [
        1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36
    ];

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ISharedPlayerManager _players = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private IServerPreferencesManager _preferences = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    private readonly Dictionary<uint, PendingSettlement> _pendingSettlements = new();
    private uint _nextSettlementId;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RouletteComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<RouletteComponent, EntityTerminatingEvent>(OnTerminating);
        SubscribeLocalEvent<PlayerDetachedEvent>(OnPlayerDetached);
        Subs.BuiEvents<RouletteComponent>(RouletteUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<RoulettePlaceBetMessage>(OnPlaceBet);
        });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        ProcessPendingSettlements(now);
        var query = EntityQueryEnumerator<RouletteComponent, RouletteVisualsComponent>();
        while (query.MoveNext(out var uid, out var roulette, out var visuals))
        {
            if (visuals.Phase == RoulettePhase.Spinning && roulette.SpinAudioStream != null)
            {
                var remaining = (float) (visuals.PhaseEndsAt - now).TotalSeconds;
                if (remaining <= SpinFadeDuration)
                {
                    var fade = Math.Clamp(remaining / SpinFadeDuration, 0f, 1f);
                    fade = MathF.Ceiling(fade * 20f) / 20f;
                    var gain = fade * fade *
                               SharedAudioSystem.VolumeToGain(SpinVolume);
                    _audio.SetGain(roulette.SpinAudioStream, gain);
                }
            }

            if (now < visuals.PhaseEndsAt)
                continue;

            var ent = new Entity<RouletteComponent, RouletteVisualsComponent>(uid, roulette, visuals);
            switch (visuals.Phase)
            {
                case RoulettePhase.Betting:
                    StartSpin(ent, now);
                    break;
                case RoulettePhase.Spinning:
                    FinishSpin(ent, now);
                    break;
                case RoulettePhase.Payout:
                    StartBetting(ent, now);
                    break;
            }
        }
    }

    private void OnMapInit(Entity<RouletteComponent> ent, ref MapInitEvent args)
    {
        if (!TryComp<RouletteVisualsComponent>(ent, out var visuals))
            return;

        StartBetting(new Entity<RouletteComponent, RouletteVisualsComponent>(ent.Owner, ent.Comp, visuals), _timing.CurTime);
    }

    private void OnUiOpened(Entity<RouletteComponent> ent, ref BoundUIOpenedEvent args)
    {
        SendUiState(ent, args.Actor);
    }

    private void OnPlaceBet(Entity<RouletteComponent> ent, ref RoulettePlaceBetMessage args)
    {
        var actor = args.Actor;
        var requestId = args.RequestId;
        if (!TryComp<RouletteVisualsComponent>(ent, out var visuals))
            return;

        if (!_players.TryGetSessionByEntity(actor, out var session))
        {
            SendBetResult(ent.Owner, actor, requestId, RouletteBetError.AccountUnavailable);
            return;
        }

        if (ent.Comp.LastRequestIds.TryGetValue(session.UserId, out var lastRequestId) && requestId <= lastRequestId)
        {
            SendBetResult(ent.Owner, actor, requestId, RouletteBetError.DuplicateRequest);
            return;
        }

        ent.Comp.LastRequestIds[session.UserId] = requestId;
        if (visuals.Phase != RoulettePhase.Betting || _timing.CurTime >= visuals.PhaseEndsAt || args.RoundId != visuals.RoundId)
        {
            RejectBet(ent.Owner, actor, requestId, RouletteBetError.BettingClosed);
            return;
        }

        if (!IsValidBet(args.Bet, ent.Comp))
        {
            RejectBet(ent.Owner, actor, requestId, RouletteBetError.InvalidBet);
            return;
        }

        var currentTotal = GetTotalBet(ent.Comp, session.UserId);
        if ((long) currentTotal + args.Bet.Amount > ent.Comp.MaximumBet)
        {
            RejectBet(ent.Owner, actor, requestId, RouletteBetError.LimitExceeded);
            return;
        }

        if (GetBetCount(ent.Comp, session.UserId) >= ent.Comp.MaximumBetsPerPlayer)
        {
            RejectBet(ent.Owner, actor, requestId, RouletteBetError.TooManyBets);
            return;
        }

        if (!_bank.TryGetBalance(actor, out var balance))
        {
            RejectBet(ent.Owner, actor, requestId, RouletteBetError.AccountUnavailable);
            return;
        }

        if (balance < args.Bet.Amount)
        {
            RejectBet(ent.Owner, actor, requestId, RouletteBetError.InsufficientFunds);
            return;
        }

        if (!_bank.TryBankWithdraw(actor, args.Bet.Amount))
        {
            RejectBet(ent.Owner, actor, requestId, RouletteBetError.AccountUnavailable);
            return;
        }

        if (!ent.Comp.PlayerSlots.ContainsKey(session.UserId))
            ent.Comp.PlayerSlots[session.UserId] = (byte) ent.Comp.PlayerSlots.Count;

        var placedAt = _timing.CurTime;
        ent.Comp.Bets.Add(new RoulettePlayerBet(session.UserId, args.Bet, placedAt));
        AddToCache(ent.Comp, session.UserId, args.Bet, placedAt);
        _audio.PlayPvs(ent.Comp.BetSound,
            ent.Owner,
            AudioParams.Default.WithVolume(-3f).WithMaxDistance(6f).WithVariation(0.08f));
        UpdateWorldBets(ent.Owner, ent.Comp, visuals);
        SendBetResult(ent.Owner, actor, requestId, RouletteBetError.None);
        UpdateOpenUis(ent.Owner, ent.Comp);
        _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
            $"{ToPrettyString(actor):actor} placed a roulette bet of {args.Bet.Amount} at {ToPrettyString(ent.Owner):entity}");
    }

    private void OnTerminating(Entity<RouletteComponent> ent, ref EntityTerminatingEvent args)
    {
        if (ent.Comp.Settled || ent.Comp.Bets.Count == 0 || !TryComp<RouletteVisualsComponent>(ent, out var visuals))
            return;

        ent.Comp.Settled = true;
        var settlements = new Dictionary<NetUserId, int>();
        for (var i = 0; i < ent.Comp.Bets.Count; i++)
        {
            var playerBet = ent.Comp.Bets[i];
            var amount = 0;
            if (visuals.Phase == RoulettePhase.Betting)
                amount = playerBet.Bet.Amount;
            else if (visuals.Phase == RoulettePhase.Spinning &&
                     TryGetPayout(playerBet.Bet, visuals.WinningNumber, out var payout))
                amount = payout;

            if (amount > 0 && !settlements.TryAdd(playerBet.Player, amount))
                settlements[playerBet.Player] += amount;
        }

        ent.Comp.Bets.Clear();
        ClearBetCaches(ent.Comp);
        ent.Comp.SpinAudioStream = _audio.Stop(ent.Comp.SpinAudioStream);
        var table = ToPrettyString(ent.Owner);
        foreach (var (playerId, amount) in settlements)
            QueueSettlement(playerId, amount, table);
    }

    private void OnPlayerDetached(PlayerDetachedEvent args)
    {
        var playerId = args.Player.UserId;
        var query = EntityQueryEnumerator<RouletteComponent, RouletteVisualsComponent>();
        while (query.MoveNext(out var uid, out var roulette, out var visuals))
        {
            if (visuals.Phase == RoulettePhase.Payout)
                continue;

            var amount = 0;
            for (var i = roulette.Bets.Count - 1; i >= 0; i--)
            {
                var playerBet = roulette.Bets[i];
                if (playerBet.Player != playerId)
                    continue;

                if (visuals.Phase == RoulettePhase.Betting)
                    amount += playerBet.Bet.Amount;
                else if (TryGetPayout(playerBet.Bet, visuals.WinningNumber, out var payout))
                    amount += payout;

                roulette.Bets.RemoveAt(i);
            }

            roulette.LastRequestIds.Remove(playerId);
            RebuildBetCaches(roulette);
            UpdateWorldBets(uid, roulette, visuals);
            UpdateOpenUis(uid, roulette);
            if (amount == 0)
                continue;

            if (!_preferences.TryGetCachedPreferences(playerId, out var prefs) ||
                prefs.SelectedCharacter is not HumanoidCharacterProfile profile ||
                !_bank.TryBankDeposit(args.Player, prefs, profile, amount, out _))
            {
                QueueSettlement(playerId, amount, ToPrettyString(uid));
                continue;
            }

            var action = visuals.Phase == RoulettePhase.Betting ? "refunded" : "paid";
            _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
                $"Roulette at {ToPrettyString(uid):entity} {action} {amount} to detached player {playerId}");
        }
    }

    private void StartBetting(Entity<RouletteComponent, RouletteVisualsComponent> ent, TimeSpan now)
    {
        ent.Comp1.Bets.Clear();
        ent.Comp1.LastRequestIds.Clear();
        ent.Comp1.LastPayouts.Clear();
        ent.Comp1.PlayerSlots.Clear();
        ent.Comp1.Settled = false;
        ClearBetCaches(ent.Comp1);
        ent.Comp2.Phase = RoulettePhase.Betting;
        ent.Comp2.PhaseStartedAt = now;
        ent.Comp2.PhaseEndsAt = now + ent.Comp1.BettingDuration;
        ent.Comp2.WinningNumber = -1;
        ent.Comp2.RoundId++;
        ent.Comp2.WorldBets = [];
        Dirty(ent.Owner, ent.Comp2);
        UpdateOpenUis(ent.Owner, ent.Comp1);
    }

    private void StartSpin(Entity<RouletteComponent, RouletteVisualsComponent> ent, TimeSpan now)
    {
        ent.Comp2.Phase = RoulettePhase.Spinning;
        ent.Comp2.PhaseStartedAt = now;
        ent.Comp2.PhaseEndsAt = now + ent.Comp1.SpinDuration;
        ent.Comp2.WinningNumber = _random.Next(37);
        Dirty(ent.Owner, ent.Comp2);
        ent.Comp1.SpinAudioStream = _audio.PlayPvs(ent.Comp1.SpinSound,
            ent.Owner,
            AudioParams.Default.WithVolume(SpinVolume).WithMaxDistance(8f))?.Entity;
        UpdateOpenUis(ent.Owner, ent.Comp1);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"Roulette at {ToPrettyString(ent.Owner):entity} started round {ent.Comp2.RoundId} with server result {ent.Comp2.WinningNumber}");
    }

    private void FinishSpin(Entity<RouletteComponent, RouletteVisualsComponent> ent, TimeSpan now)
    {
        ent.Comp1.SpinAudioStream = _audio.Stop(ent.Comp1.SpinAudioStream);
        ent.Comp1.Settled = true;
        CalculatePayouts(ent.Owner, ent.Comp1, ent.Comp2.WinningNumber);
        AnnounceResult(ent.Owner, ent.Comp2.WinningNumber);
        ent.Comp2.Phase = RoulettePhase.Payout;
        ent.Comp2.PhaseStartedAt = now;
        ent.Comp2.PhaseEndsAt = now + ent.Comp1.PayoutDuration;
        Dirty(ent.Owner, ent.Comp2);
        UpdateOpenUis(ent.Owner, ent.Comp1);
        _adminLog.Add(LogType.Action, LogImpact.Medium,
            $"Roulette at {ToPrettyString(ent.Owner):entity} finished round {ent.Comp2.RoundId} on {ent.Comp2.WinningNumber} with {ent.Comp1.Bets.Count} bets");
    }

    private void AnnounceResult(EntityUid uid, int winningNumber)
    {
        var message = winningNumber == 0
            ? Loc.GetString("roulette-announcement-zero")
            : Loc.GetString("roulette-announcement-result",
                ("number", winningNumber),
                ("color", Loc.GetString(RedNumbers.Contains(winningNumber)
                    ? "roulette-color-red"
                    : "roulette-color-black")));
        _chat.TrySendInGameICMessage(uid,
            message,
            InGameICChatType.Speak,
            hideChat: false,
            checkRadioPrefix: false,
            ignoreActionBlocker: true);
    }

    private void CalculatePayouts(EntityUid uid, RouletteComponent roulette, int winningNumber)
    {
        for (var i = 0; i < roulette.Bets.Count; i++)
        {
            var playerBet = roulette.Bets[i];
            if (!TryGetPayout(playerBet.Bet, winningNumber, out var payout))
                continue;

            if (!roulette.LastPayouts.TryAdd(playerBet.Player, payout))
                roulette.LastPayouts[playerBet.Player] += payout;
        }

        foreach (var (playerId, payout) in roulette.LastPayouts)
        {
            QueueSettlement(playerId, payout, ToPrettyString(uid));
        }
    }

    private void UpdateOpenUis(EntityUid uid, RouletteComponent roulette)
    {
        foreach (var actor in _ui.GetActors(uid, RouletteUiKey.Key))
        {
            if (Exists(actor))
                SendUiState(new Entity<RouletteComponent>(uid, roulette), actor);
        }
    }

    private void SendUiState(Entity<RouletteComponent> ent, EntityUid actor)
    {
        if (!TryComp<RouletteVisualsComponent>(ent, out var visuals) ||
            !_players.TryGetSessionByEntity(actor, out var session))
        {
            return;
        }

        var playerBets = ent.Comp.PlayerBets.GetValueOrDefault(session.UserId);
        var bets = playerBets?.Bets ?? [];
        var total = playerBets?.Total ?? 0;

        _bank.TryGetBalance(actor, out var balance);
        ent.Comp.LastPayouts.TryGetValue(session.UserId, out var lastPayout);
        var state = new RouletteUiState(
            visuals.Phase,
            visuals.PhaseStartedAt,
            visuals.PhaseEndsAt,
            visuals.WinningNumber,
            visuals.RoundId,
            balance,
            total,
            lastPayout,
            ent.Comp.MinimumBet,
            ent.Comp.MaximumBet,
            ent.Comp.MaximumBetsPerPlayer,
            ent.Comp.BettingDuration,
            ent.Comp.SpinDuration,
            ent.Comp.PayoutDuration,
            bets,
            ent.Comp.PlayerBetSummaries);
        _ui.ServerSendUiMessage(ent.Owner, RouletteUiKey.Key, new RouletteStateMessage(state), actor);
    }

    private void RejectBet(EntityUid uid, EntityUid actor, uint requestId, RouletteBetError error)
    {
        SendBetResult(uid, actor, requestId, error);
        _popup.PopupEntity(Loc.GetString(GetErrorLocId(error)), uid, actor);
    }

    private void SendBetResult(EntityUid uid, EntityUid actor, uint requestId, RouletteBetError error)
    {
        _ui.ServerSendUiMessage(uid, RouletteUiKey.Key, new RouletteBetResultMessage(error, requestId), actor);
    }

    private static bool IsValidBet(RouletteBet bet, RouletteComponent roulette)
    {
        if (!Enum.IsDefined(bet.Type) || bet.Amount < roulette.MinimumBet || bet.Amount > roulette.MaximumBet)
            return false;

        return bet.Type != RouletteBetType.Number || bet.Number is >= 0 and <= 36;
    }

    private static int GetTotalBet(RouletteComponent roulette, NetUserId player)
    {
        return roulette.PlayerBets.TryGetValue(player, out var cache) ? cache.Total : 0;
    }

    private static int GetBetCount(RouletteComponent roulette, NetUserId player)
    {
        return roulette.PlayerBets.TryGetValue(player, out var cache) ? cache.Bets.Length : 0;
    }

    private void UpdateWorldBets(EntityUid uid, RouletteComponent roulette, RouletteVisualsComponent visuals)
    {
        visuals.WorldBets = roulette.WorldBets;
        Dirty(uid, visuals);
    }

    private void AddToCache(RouletteComponent roulette, NetUserId player, RouletteBet bet, TimeSpan placedAt)
    {
        if (!roulette.PlayerBets.TryGetValue(player, out var playerCache))
        {
            playerCache = new RoulettePlayerCache
            {
                SummaryIndex = roulette.PlayerBetSummaries.Length
            };
            roulette.PlayerBets.Add(player, playerCache);
            Array.Resize(ref roulette.PlayerBetSummaries, roulette.PlayerBetSummaries.Length + 1);
        }

        Array.Resize(ref playerCache.Bets, playerCache.Bets.Length + 1);
        playerCache.Bets[^1] = bet;
        playerCache.Total += bet.Amount;
        roulette.PlayerBetSummaries[playerCache.SummaryIndex] = new RoulettePlayerBetSummary(
            GetPlayerName(player),
            playerCache.Total);

        var number = bet.Type == RouletteBetType.Number ? bet.Number : -1;
        var key = (player, bet.Type, number);
        if (roulette.WorldBetIndices.TryGetValue(key, out var worldBetIndex))
        {
            var worldBet = roulette.WorldBets[worldBetIndex];
            roulette.WorldBets[worldBetIndex] = worldBet with
            {
                Amount = worldBet.Amount + bet.Amount,
                PlacedAt = placedAt
            };
            return;
        }

        var index = roulette.WorldBets.Length;
        roulette.WorldBetIndices.Add(key, index);
        Array.Resize(ref roulette.WorldBets, index + 1);
        roulette.WorldBets[index] = new RouletteWorldBet(
            GetPlayerName(player),
            bet.Type,
            number,
            bet.Amount,
            roulette.PlayerSlots[player],
            placedAt);
    }

    private void RebuildBetCaches(RouletteComponent roulette)
    {
        ClearBetCaches(roulette);
        for (var i = 0; i < roulette.Bets.Count; i++)
        {
            var playerBet = roulette.Bets[i];
            AddToCache(roulette, playerBet.Player, playerBet.Bet, playerBet.PlacedAt);
        }
    }

    private static void ClearBetCaches(RouletteComponent roulette)
    {
        roulette.PlayerBets.Clear();
        roulette.WorldBetIndices.Clear();
        roulette.WorldBets = [];
        roulette.PlayerBetSummaries = [];
    }

    private void QueueSettlement(NetUserId playerId, int amount, string table)
    {
        var id = ++_nextSettlementId;
        var settlement = new PendingSettlement(playerId, amount, table, _timing.CurTime);
        _pendingSettlements.Add(id, settlement);
        TrySettle(id, settlement);
    }

    private void ProcessPendingSettlements(TimeSpan now)
    {
        if (_pendingSettlements.Count == 0)
            return;

        var due = new List<(uint Id, PendingSettlement Settlement)>();
        foreach (var (id, settlement) in _pendingSettlements)
        {
            if (!settlement.Processing && settlement.NextAttempt <= now)
                due.Add((id, settlement));
        }

        for (var i = 0; i < due.Count; i++)
            TrySettle(due[i].Id, due[i].Settlement);
    }

    private async void TrySettle(uint id, PendingSettlement settlement)
    {
        settlement.Processing = true;
        try
        {
            if (_players.TryGetSessionById(settlement.Player, out var session) &&
                session.AttachedEntity is { } player &&
                _bank.TryBankDeposit(player, settlement.Amount))
            {
                CompleteSettlement(id, settlement);
                return;
            }

            if (_preferences.TryGetCachedPreferences(settlement.Player, out var prefs) &&
                prefs.SelectedCharacter is HumanoidCharacterProfile profile &&
                await _bank.TryBankDepositOffline(settlement.Player, prefs, profile, settlement.Amount))
            {
                CompleteSettlement(id, settlement);
                return;
            }
        }
        catch (Exception exception)
        {
            Logger.Error($"Failed roulette settlement for {settlement.Player}: {exception}");
        }

        settlement.Processing = false;
        settlement.NextAttempt = _timing.CurTime + SettlementRetryDelay;
    }

    private void CompleteSettlement(uint id, PendingSettlement settlement)
    {
        _pendingSettlements.Remove(id);
        _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
            $"Roulette at {settlement.Table} settled {settlement.Amount} to player {settlement.Player}");
    }

    private string GetPlayerName(NetUserId playerId)
    {
        if (!_players.TryGetSessionById(playerId, out var session))
            return Loc.GetString("roulette-player-unavailable");

        return session.AttachedEntity is { } player
            ? Identity.Name(player, EntityManager)
            : session.Name;
    }

    private static bool TryGetPayout(RouletteBet bet, int winningNumber, out int payout)
    {
        payout = 0;
        var won = bet.Type switch
        {
            RouletteBetType.Number => bet.Number == winningNumber,
            RouletteBetType.Red => RedNumbers.Contains(winningNumber),
            RouletteBetType.Black => winningNumber != 0 && !RedNumbers.Contains(winningNumber),
            RouletteBetType.Even => winningNumber != 0 && winningNumber % 2 == 0,
            RouletteBetType.Odd => winningNumber % 2 != 0,
            RouletteBetType.Low => winningNumber is >= 1 and <= 18,
            RouletteBetType.High => winningNumber is >= 19 and <= 36,
            RouletteBetType.FirstDozen => winningNumber is >= 1 and <= 12,
            RouletteBetType.SecondDozen => winningNumber is >= 13 and <= 24,
            RouletteBetType.ThirdDozen => winningNumber is >= 25 and <= 36,
            _ => false
        };

        if (!won)
            return false;

        var multiplier = bet.Type switch
        {
            RouletteBetType.Number => 36,
            RouletteBetType.FirstDozen or RouletteBetType.SecondDozen or RouletteBetType.ThirdDozen => 3,
            _ => 2
        };
        var calculated = (long) bet.Amount * multiplier;
        payout = calculated > int.MaxValue ? int.MaxValue : (int) calculated;
        return true;
    }

    private static string GetErrorLocId(RouletteBetError error)
    {
        return error switch
        {
            RouletteBetError.BettingClosed => "roulette-error-betting-closed",
            RouletteBetError.InvalidBet => "roulette-error-invalid-bet",
            RouletteBetError.InsufficientFunds => "roulette-error-insufficient-funds",
            RouletteBetError.DuplicateRequest => "roulette-error-duplicate-request",
            RouletteBetError.AccountUnavailable => "roulette-error-account-unavailable",
            RouletteBetError.LimitExceeded => "roulette-error-limit-exceeded",
            RouletteBetError.TooManyBets => "roulette-error-too-many-bets",
            _ => "roulette-error-invalid-bet"
        };
    }

    private sealed class PendingSettlement(
        NetUserId player,
        int amount,
        string table,
        TimeSpan nextAttempt)
    {
        public readonly NetUserId Player = player;
        public readonly int Amount = amount;
        public readonly string Table = table;
        public TimeSpan NextAttempt = nextAttempt;
        public bool Processing;
    }
}
