using Content.Server._Mono.AlertLevel;
using Content.Server._NF.SectorServices;
using Content.Server.Administration.Logs;
using Content.Server.Chat.Systems;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Territory;
using Content.Shared.Database;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.War;

/// <summary>
/// Owns the sector-wide, pairwise war state and its announcements.
/// </summary>
public sealed partial class FactionWarSystem : EntitySystem
{
    private static readonly SoundSpecifier DeclarationSound =
        new SoundPathSpecifier("/Audio/Misc/gamma.ogg");

    private static readonly SoundSpecifier PeaceSound =
        new SoundPathSpecifier("/Audio/Misc/notice2.ogg");

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private GameTicker _ticker = default!;
    [Dependency] private SectorServiceSystem _sectorService = default!;

    public bool IsRoundRunning => _ticker.RunLevel == GameRunLevel.InRound;

    public bool TryGetState(out Entity<WarLevelComponent> state)
    {
        state = default;
        var uid = _sectorService.GetServiceEntity();

        if (!uid.Valid || !TryComp(uid, out WarLevelComponent? component))
            return false;

        state = (uid, component);
        return true;
    }

    public TimeSpan GetDeclarationAvailableAt(Entity<WarLevelComponent> state)
    {
        return _ticker.RoundStartTimeSpan + state.Comp.DeclarationDelay;
    }

    public void GetConfiguredTargets(
        Entity<WarLevelComponent> state,
        Entity<WarDeclarationConsoleComponent> console,
        List<ProtoId<TerritoryFactionPrototype>> output)
    {
        output.Clear();

        if (!state.Comp.Factions.Contains(console.Comp.Faction))
            return;

        foreach (var faction in state.Comp.Factions)
        {
            if (faction == console.Comp.Faction ||
                console.Comp.Targets.Count != 0 && !console.Comp.Targets.Contains(faction))
            {
                continue;
            }

            output.Add(faction);
        }
    }

    public bool IsConfiguredTarget(
        Entity<WarLevelComponent> state,
        Entity<WarDeclarationConsoleComponent> console,
        ProtoId<TerritoryFactionPrototype> target)
    {
        return target != console.Comp.Faction &&
               state.Comp.Factions.Contains(console.Comp.Faction) &&
               state.Comp.Factions.Contains(target) &&
               (console.Comp.Targets.Count == 0 || console.Comp.Targets.Contains(target));
    }

    public bool TryGetDeclaration(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second,
        out FactionWarDeclaration declaration)
    {
        foreach (var candidate in state.Comp.Declarations)
        {
            if (candidate.DeclaringFaction == first && candidate.TargetFaction == second ||
                candidate.DeclaringFaction == second && candidate.TargetFaction == first)
            {
                declaration = candidate;
                return true;
            }
        }

        declaration = null!;
        return false;
    }

    public WarDeclarationResult TryDeclareWar(
        ProtoId<TerritoryFactionPrototype> declarer,
        ProtoId<TerritoryFactionPrototype> target,
        EntityUid? actor = null,
        EntityUid? source = null,
        bool force = false)
    {
        if (!TryGetState(out var state))
            return WarDeclarationResult.StateUnavailable;

        var validation = ValidatePair(state, declarer, target);
        if (validation != WarDeclarationResult.Success)
            return validation;

        if (TryGetDeclaration(state, declarer, target, out _))
            return WarDeclarationResult.AlreadyAtWar;

        if (!force)
        {
            if (!IsRoundRunning)
                return WarDeclarationResult.RoundNotRunning;

            if (_ticker.RoundDuration() < state.Comp.DeclarationDelay)
                return WarDeclarationResult.TooEarly;

            if (TryGetWarCooldown(state, declarer, target, out var cooldown) &&
                _ticker.RoundDuration() < cooldown.AvailableAtRoundTime)
            {
                return WarDeclarationResult.PostWarCooldown;
            }
        }

        var roundTime = _ticker.RunLevel == GameRunLevel.InRound
            ? _ticker.RoundDuration()
            : TimeSpan.Zero;
        var declaration = new FactionWarDeclaration(declarer, target, roundTime);

        if (TryGetWarCooldown(state, declarer, target, out var previousCooldown))
            state.Comp.WarCooldowns.Remove(previousCooldown);

        state.Comp.Declarations.Add(declaration);

        AnnounceDeclaration(declaration);
        LogDeclaration(declaration, actor, source);
        RaiseLocalEvent(new WarLevelChangedEvent(true));
        return WarDeclarationResult.Success;
    }

    public WarDeclarationResult TryEndWar(
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second,
        EntityUid? actor = null,
        EntityUid? source = null,
        bool force = false,
        bool announce = true)
    {
        if (!TryGetState(out var state))
            return WarDeclarationResult.StateUnavailable;

        var validation = ValidatePair(state, first, second);
        if (validation != WarDeclarationResult.Success)
            return validation;

        if (!force && !IsRoundRunning)
            return WarDeclarationResult.RoundNotRunning;

        if (!TryGetDeclaration(state, first, second, out var declaration))
            return WarDeclarationResult.NotAtWar;

        StartWarCooldown(state, declaration);
        state.Comp.Declarations.Remove(declaration);

        if (announce)
            AnnounceWarEnded(declaration);

        LogWarEnded(declaration, actor, source);
        RaiseLocalEvent(new WarLevelChangedEvent(state.Comp.PostWar));
        return WarDeclarationResult.Success;
    }

    public bool ClearAllWars(EntityUid? actor = null, EntityUid? source = null, bool announce = true)
    {
        if (!TryGetState(out var state) || state.Comp.Declarations.Count == 0)
            return false;

        var declarationCount = state.Comp.Declarations.Count;
        foreach (var declaration in state.Comp.Declarations)
            StartWarCooldown(state, declaration);

        state.Comp.Declarations.Clear();

        if (announce)
        {
            _chat.DispatchGlobalAnnouncement(
                Loc.GetString("war-declaration-cleared-all-announcement"),
                sender: Loc.GetString("war-declaration-announcement-sender"),
                announcementSound: PeaceSound,
                colorOverride: Color.CornflowerBlue);
        }

        LogAllWarsCleared(declarationCount, actor, source);
        RaiseLocalEvent(new WarLevelChangedEvent(false));
        return true;
    }

    public string GetFactionName(ProtoId<TerritoryFactionPrototype> faction)
    {
        return _prototype.TryIndex(faction, out var prototype)
            ? Loc.GetString(prototype.DisplayName ?? prototype.RadarLabel)
            : faction.Id;
    }

    private WarDeclarationResult ValidatePair(
        Entity<WarLevelComponent> state,
        ProtoId<TerritoryFactionPrototype> first,
        ProtoId<TerritoryFactionPrototype> second)
    {
        if (!state.Comp.Factions.Contains(first) ||
            !state.Comp.Factions.Contains(second) ||
            !_prototype.HasIndex(first) ||
            !_prototype.HasIndex(second))
        {
            return WarDeclarationResult.InvalidFaction;
        }

        if (first == second)
            return WarDeclarationResult.SameFaction;

        return WarDeclarationResult.Success;
    }

    private void AnnounceDeclaration(FactionWarDeclaration declaration)
    {
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("war-declaration-announcement",
                ("declarer", GetFactionName(declaration.DeclaringFaction)),
                ("target", GetFactionName(declaration.TargetFaction))),
            sender: Loc.GetString("war-declaration-announcement-sender"),
            announcementSound: DeclarationSound,
            colorOverride: Color.Crimson);
    }

    private void AnnounceWarEnded(FactionWarDeclaration declaration)
    {
        _chat.DispatchGlobalAnnouncement(
            Loc.GetString("war-declaration-ended-announcement",
                ("declarer", GetFactionName(declaration.DeclaringFaction)),
                ("target", GetFactionName(declaration.TargetFaction))),
            sender: Loc.GetString("war-declaration-announcement-sender"),
            announcementSound: PeaceSound,
            colorOverride: Color.CornflowerBlue);
    }

    private void LogDeclaration(
        FactionWarDeclaration declaration,
        EntityUid? actor,
        EntityUid? source)
    {
        var roundTime = GetRoundTimeForLog();

        if (actor is { Valid: true } actorUid && source is { Valid: true } sourceUid)
        {
            _adminLog.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(actorUid):player} declared war as {declaration.DeclaringFaction.Id} against {declaration.TargetFaction.Id} using {ToPrettyString(sourceUid):entity} at round time {roundTime}");
            return;
        }

        if (actor is { Valid: true } actorOnly)
        {
            _adminLog.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(actorOnly):player} declared war as {declaration.DeclaringFaction.Id} against {declaration.TargetFaction.Id} at round time {roundTime}");
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"System/admin declared war as {declaration.DeclaringFaction.Id} against {declaration.TargetFaction.Id} at round time {roundTime}");
    }

    private void LogWarEnded(
        FactionWarDeclaration declaration,
        EntityUid? actor,
        EntityUid? source)
    {
        var roundTime = GetRoundTimeForLog();

        if (actor is { Valid: true } actorUid && source is { Valid: true } sourceUid)
        {
            _adminLog.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(actorUid):player} ended the war declared by {declaration.DeclaringFaction.Id} against {declaration.TargetFaction.Id} using {ToPrettyString(sourceUid):entity} at round time {roundTime}");
            return;
        }

        if (actor is { Valid: true } actorOnly)
        {
            _adminLog.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(actorOnly):player} ended the war declared by {declaration.DeclaringFaction.Id} against {declaration.TargetFaction.Id} at round time {roundTime}");
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"System/admin ended the war declared by {declaration.DeclaringFaction.Id} against {declaration.TargetFaction.Id} at round time {roundTime}");
    }

    private void LogAllWarsCleared(int declarationCount, EntityUid? actor, EntityUid? source)
    {
        var roundTime = GetRoundTimeForLog();

        if (actor is { Valid: true } actorUid && source is { Valid: true } sourceUid)
        {
            _adminLog.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(actorUid):player} cleared all {declarationCount} active faction wars using {ToPrettyString(sourceUid):entity} at round time {roundTime}");
            return;
        }

        if (actor is { Valid: true } actorOnly)
        {
            _adminLog.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(actorOnly):player} cleared all {declarationCount} active faction wars at round time {roundTime}");
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"System/admin cleared all {declarationCount} active faction wars at round time {roundTime}");
    }

    private TimeSpan GetRoundTimeForLog()
    {
        return _ticker.RunLevel == GameRunLevel.InRound
            ? _ticker.RoundDuration()
            : TimeSpan.Zero;
    }
}
