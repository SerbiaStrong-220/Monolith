using System.Threading.Tasks;
using Content.Server._Mono.MonoCoins;
using Content.Server.Administration.Logs;
using Content.Server.Afk;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Shared._CorvaxNext.Silicons.Borgs.Components;
using Content.Shared._NF.Bank;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Content.Shared.Mind;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Silicons.StationAi;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.PlaytimeSalary;

public sealed class PlaytimeSalarySystem : EntitySystem
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(1);

    [Dependency] private IAfkManager _afk = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private MonoCoinsManager _coins = default!;
    [Dependency] private SharedStationAiSystem _stationAi = default!;

    private EntityQuery<PlaytimeSalaryComponent> _salaryQuery;
    private EntityQuery<MetaDataComponent> _metadataQuery;
    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<MindContainerComponent> _mindContainerQuery;
    private EntityQuery<MindComponent> _mindQuery;
    private EntityQuery<StationAiHeldComponent> _aiQuery;
    private EntityQuery<AiRemoteControllerComponent> _remoteQuery;

    public override void Initialize()
    {
        base.Initialize();
        _salaryQuery = GetEntityQuery<PlaytimeSalaryComponent>();
        _metadataQuery = GetEntityQuery<MetaDataComponent>();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _mindContainerQuery = GetEntityQuery<MindContainerComponent>();
        _mindQuery = GetEntityQuery<MindComponent>();
        _aiQuery = GetEntityQuery<StationAiHeldComponent>();
        _remoteQuery = GetEntityQuery<AiRemoteControllerComponent>();
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnRoundStarted(RoundStartedEvent args)
    {
        var uid = Spawn(null, MapCoordinates.Nullspace);
        var ledger = AddComp<PlaytimeSalaryLedgerComponent>(uid);
        ledger.LastCheck = _timing.CurTime;
        ledger.NextCheck = _timing.CurTime;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var query = AllEntityQuery<PlaytimeSalaryLedgerComponent>();
        while (query.MoveNext(out var uid, out _))
            QueueDel(uid);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_ticker.RunLevel != GameRunLevel.InRound)
            return;

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<PlaytimeSalaryLedgerComponent>();
        while (query.MoveNext(out _, out var ledger))
        {
            if (now < ledger.NextCheck)
                continue;

            ledger.NextCheck += TimeSpan.FromTicks(((now - ledger.NextCheck).Ticks / CheckInterval.Ticks + 1) * CheckInterval.Ticks);
            foreach (var session in _players.Sessions)
            {
                if (!TryGetSalary(session, out var salaryId) ||
                    !_prototypes.TryIndex(salaryId, out var salary) ||
                    salary.HourlyRate <= 0 || salary.PayoutInterval <= TimeSpan.Zero)
                {
                    continue;
                }

                var key = (session.UserId, salaryId);
                if (!ledger.Accounts.TryGetValue(key, out var account))
                {
                    account = new PlaytimeSalaryAccount();
                    ledger.Accounts.Add(key, account);
                }
                else if (account.LastEligible == ledger.LastCheck)
                {
                    // Both samples must be eligible. Never catch up time spent offline, AFK or ghosted.
                    account.UnpaidTime += now - ledger.LastCheck;
                }

                account.LastEligible = now;
                if (!account.PaymentPending && now >= account.NextPaymentAttempt && account.UnpaidTime >= salary.PayoutInterval)
                {
                    account.PaymentPending = true;
                    _ = PayAsync(session.UserId, account, salary);
                }
            }

            ledger.LastCheck = now;
        }
    }

    private bool TryGetSalary(ICommonSession session, out ProtoId<PlaytimeSalaryPrototype> salary)
    {
        salary = default;
        if (session.Status != SessionStatus.InGame || session.AttachedEntity is not { } body ||
            !IsLivingBody(body) || _afk.IsAfk(session) ||
            !_mindContainerQuery.TryGetComponent(body, out var container) || container.Mind is not { } mindUid ||
            !_mindQuery.TryGetComponent(mindUid, out var mind) || mind.UserId != session.UserId ||
            mind.IsVisitingEntity || mind.OwnedEntity != body)
        {
            return false;
        }

        if (_remoteQuery.TryGetComponent(body, out var remote) &&
            (remote.AiHolder != null || remote.LinkedMind != null))
        {
            // A remote body cannot turn an excluded AI into a paid one, or pay both the AI and its shell.
            return _salaryQuery.HasComponent(body) && remote.LinkedMind == mindUid &&
                   remote.AiHolder is { } brain && IsLivingBody(brain) &&
                   _aiQuery.TryGetComponent(brain, out var held) && held.CurrentConnectedEntity == body &&
                   TryGetCoreSalary(brain, out salary);
        }

        if (_aiQuery.HasComponent(body))
            return TryGetCoreSalary(body, out salary);

        if (!_salaryQuery.TryGetComponent(body, out var component))
            return false;

        salary = component.Salary;
        return true;
    }

    private bool TryGetCoreSalary(EntityUid brain, out ProtoId<PlaytimeSalaryPrototype> salary)
    {
        salary = default;
        if (!_stationAi.TryGetCore(brain, out var core) || !IsAvailable(core.Owner) ||
            !_salaryQuery.TryGetComponent(core.Owner, out var component))
        {
            return false;
        }

        salary = component.Salary;
        return true;
    }

    private bool IsAvailable(EntityUid uid)
    {
        return _metadataQuery.TryGetComponent(uid, out var metadata) && metadata.EntityInitialized &&
               metadata.EntityLifeStage < EntityLifeStage.Terminating && !metadata.EntityPaused;
    }

    private bool IsLivingBody(EntityUid uid)
    {
        return IsAvailable(uid) && _mobQuery.TryGetComponent(uid, out var mob) && mob.CurrentState != MobState.Dead;
    }

    private async Task PayAsync(NetUserId user, PlaytimeSalaryAccount account, PlaytimeSalaryPrototype salary)
    {
        var intervals = account.UnpaidTime.Ticks / salary.PayoutInterval.Ticks;
        var paidTime = TimeSpan.FromTicks(intervals * salary.PayoutInterval.Ticks);
        var numerator = (decimal) salary.HourlyRate * paidTime.Ticks + account.Remainder;
        var amount = (long) decimal.Floor(numerator / TimeSpan.TicksPerHour);
        var remainder = numerator % TimeSpan.TicksPerHour;

        try
        {
            if (amount > 0 && await _coins.AddMonoCoinsAsync(user, amount) == 0)
                throw new InvalidOperationException($"No savings account found for {user}.");
        }
        catch (Exception exception)
        {
            account.NextPaymentAttempt = _timing.CurTime + RetryInterval;
            Log.Error($"Could not pay playtime salary {salary.ID} to {user}: {exception}");
            return;
        }
        finally
        {
            account.PaymentPending = false;
        }

        // The player may have changed body or disconnected during the DB write. Only account data is retained.
        account.UnpaidTime -= paidTime;
        account.Remainder = remainder;
        if (amount <= 0)
            return;

        try
        {
            _adminLog.Add(LogType.Action, LogImpact.Medium,
                $"Player {user} received {amount} savings credits for {paidTime} of {salary.ID} playtime.");
            if (_players.TryGetSessionById(user, out var session) && session.Status != SessionStatus.Disconnected)
            {
                _chat.DispatchServerMessage(session, Loc.GetString(salary.PaymentMessage,
                    ("amount", BankSystemExtensions.ToSpesoString(amount)), ("minutes", paidTime.TotalMinutes)));
            }
        }
        catch (Exception exception)
        {
            // A notification failure must never retry an already committed payment.
            Log.Error($"Playtime salary was paid to {user}, but its notification failed: {exception}");
        }
    }
}
