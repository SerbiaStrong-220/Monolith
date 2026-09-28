using Content.Server._NF.SectorServices;
using Content.Server.Chat.Systems;
using Content.Server.Administration.Logs;
using Content.Shared.Database;
using Content.Server.GameTicking;
using Content.Shared._Exodus.War;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.War;

/// <summary>
/// Runs the two-stage transition for the global faction escalation code.
/// The old code remains active until the transition timer completes.
/// </summary>
public sealed partial class FactionAlertLevelSystem : EntitySystem
{
    private static readonly SoundSpecifier AnnouncementSound =
        new SoundPathSpecifier("/Audio/Misc/notice1.ogg");

    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FactionAlertLevelComponent, ComponentInit>(OnInit);
    }

    public override void Update(float frameTime)
    {
        if (_ticker.RunLevel != GameRunLevel.InRound || !TryGetState(out var state))
            return;

        var comp = state.Comp;
        InitializeRound(state);

        if (comp.PendingLevel is not { } pending || _timing.CurTime < comp.PendingAt)
            return;

        if (!_prototypes.TryIndex(comp.Prototype, out FactionAlertLevelPrototype? prototype) ||
            !prototype.Levels.TryGetValue(pending, out var detail))
        {
            Log.Error($"Pending faction code {pending} no longer exists in {comp.Prototype}.");
            comp.PendingLevel = null;
            var cancelled = new FactionAlertLevelChangedEvent();
            RaiseLocalEvent(ref cancelled);
            return;
        }

        comp.CurrentLevel = pending;
        comp.PendingLevel = null;
        comp.NextChangeAt = _timing.CurTime + detail.MinimumDuration;
        Announce(detail.EndAnnouncement);
        var changed = new FactionAlertLevelChangedEvent();
        RaiseLocalEvent(ref changed);
    }

    public bool TryGetState(out Entity<FactionAlertLevelComponent> state)
    {
        var service = _sectorService.GetServiceEntity();
        if (service.Valid && TryComp(service, out FactionAlertLevelComponent? component))
        {
            state = (service, component);
            return true;
        }

        state = default;
        return false;
    }

    public bool TrySetLevel(string level, out FactionAlertLevelSetResult result, EntityUid? actor = null, EntityUid? console = null)
    {
        result = FactionAlertLevelSetResult.StateUnavailable;
        if (string.IsNullOrWhiteSpace(level) || !TryGetState(out var state) ||
            !_prototypes.TryIndex(state.Comp.Prototype, out FactionAlertLevelPrototype? prototype) ||
            !prototype.Levels.TryGetValue(level, out var detail))
        {
            return false;
        }

        if (_ticker.RunLevel != GameRunLevel.InRound)
        {
            result = FactionAlertLevelSetResult.RoundNotRunning;
            return false;
        }

        InitializeRound(state);

        if (state.Comp.PendingLevel != null)
        {
            result = FactionAlertLevelSetResult.TransitionInProgress;
            return false;
        }

        if (_timing.CurTime < state.Comp.NextChangeAt)
        {
            result = FactionAlertLevelSetResult.Cooldown;
            return false;
        }

        if (state.Comp.CurrentLevel == level)
        {
            result = FactionAlertLevelSetResult.AlreadyActive;
            return false;
        }

        if (!IsNextLevel(prototype, state.Comp.CurrentLevel, level))
        {
            result = FactionAlertLevelSetResult.NotNextLevel;
            return false;
        }

        state.Comp.PendingLevel = level;
        state.Comp.PendingAt = _timing.CurTime + detail.TransitionDuration;
        Announce(detail.StartAnnouncement);
        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(actor):player} started transition to faction code {level} using {ToPrettyString(console):entity}");
        var changed = new FactionAlertLevelChangedEvent();
        RaiseLocalEvent(ref changed);
        result = FactionAlertLevelSetResult.Success;
        return true;
    }

    private static bool IsNextLevel(FactionAlertLevelPrototype prototype, string currentLevel, string targetLevel)
    {
        if (!prototype.Levels.TryGetValue(currentLevel, out var current) ||
            !prototype.Levels.TryGetValue(targetLevel, out var target) || target.Order <= current.Order)
        {
            return false;
        }

        foreach (var level in prototype.Levels.Values)
        {
            if (level.Order > current.Order && level.Order < target.Order)
                return false;
        }

        return true;
    }

    private void InitializeRound(Entity<FactionAlertLevelComponent> state)
    {
        if (state.Comp.RoundInitialized || _ticker.RunLevel != GameRunLevel.InRound ||
            !_prototypes.TryIndex(state.Comp.Prototype, out var prototype))
            return;

        state.Comp.RoundInitialized = true;
        state.Comp.NextChangeAt = _ticker.RoundStartTimeSpan + prototype.InitialMinimumDuration;
    }

    private void OnInit(Entity<FactionAlertLevelComponent> ent, ref ComponentInit args)
    {
        if (!_prototypes.TryIndex(ent.Comp.Prototype, out FactionAlertLevelPrototype? prototype))
            return;

        ent.Comp.CurrentLevel = prototype.DefaultLevel;
        ent.Comp.PendingLevel = null;
        ent.Comp.NextChangeAt = TimeSpan.MaxValue;
        ent.Comp.RoundInitialized = false;
    }

    public bool TryCopyState(out FactionAlertLevelState? state)
    {
        state = null;
        if (!TryGetState(out var entity) ||
            !_prototypes.TryIndex(entity.Comp.Prototype, out FactionAlertLevelPrototype? prototype))
        {
            return false;
        }

        InitializeRound(entity);

        var ordered = new List<(int Order, string Id, FactionAlertLevelDetail Detail)>(prototype.Levels.Count);
        foreach (var (id, detail) in prototype.Levels)
        {
            ordered.Add((detail.Order, id, detail));
        }

        ordered.Sort((left, right) => left.Order.CompareTo(right.Order));

        var levels = new List<FactionAlertLevelOptionState>(ordered.Count);
        foreach (var entry in ordered)
        {
            levels.Add(new FactionAlertLevelOptionState(
                entry.Id,
                entry.Detail.Name,
                entry.Detail.Description,
                entry.Detail.Color,
                IsNextLevel(prototype, entity.Comp.CurrentLevel, entry.Id)));
        }

        state = new FactionAlertLevelState(
            levels,
            entity.Comp.CurrentLevel,
            entity.Comp.PendingLevel,
            entity.Comp.NextChangeAt,
            entity.Comp.PendingAt);
        return true;
    }

    private void Announce(LocId message)
    {
        if (string.IsNullOrEmpty(message))
            return;

        _chat.DispatchGlobalAnnouncement(
            Loc.GetString(message),
            sender: Loc.GetString("faction-alert-announcement-sender"),
            announcementSound: AnnouncementSound,
            colorOverride: Color.Crimson);
    }
}

public enum FactionAlertLevelSetResult : byte
{
    Success,
    StateUnavailable,
    RoundNotRunning,
    TransitionInProgress,
    Cooldown,
    AlreadyActive,
    NotNextLevel,
}

[ByRefEvent]
public readonly record struct FactionAlertLevelChangedEvent;
