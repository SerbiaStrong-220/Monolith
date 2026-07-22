using Content.Server.Administration.Logs;
using Content.Server.Chat.Managers;
using Content.Shared._Exodus.Territory;
using Content.Shared.Database;
using Content.Shared.GameTicking;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

public sealed partial class GridTerritoryAdminLogSystem : EntitySystem
{
    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IChatManager _chat = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private bool _suppressLogs;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<GridTerritoryControllerChangedEvent>(OnControllerChanged);
        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStarted);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnControllerChanged(ref GridTerritoryControllerChangedEvent args)
    {
        if (_suppressLogs)
            return;

        if (args.NewFaction is { } newFaction)
        {
            // Actorless claims are map-init/admin/system changes; only player-driven claims need admin logs.
            if (args.Actor is { } actor)
                LogClaim(args.Grid, newFaction, args.SourceBanner, actor);

            return;
        }

        if (args.OldFaction is { } oldFaction)
            LogUnclaim(args.Grid, oldFaction, args.OldSourceBanner);
    }

    private void OnRoundStarted(RoundStartedEvent ev)
    {
        _suppressLogs = false;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        _suppressLogs = true;
    }

    private void LogClaim(
        EntityUid grid,
        ProtoId<TerritoryFactionPrototype> faction,
        EntityUid? banner,
        EntityUid actor)
    {
        if (banner is { } bannerUid && TryComp<TransformComponent>(bannerUid, out var bannerXform))
        {
            var bannerCoordinates = _transform.GetMapCoordinates(bannerXform);

            _adminLog.Add(LogType.Action, LogImpact.High,
                $"{ToPrettyString(actor):user} claimed territory {ToPrettyString(grid):entity} for {faction.Id} using {ToPrettyString(bannerUid):entity} at {bannerCoordinates:coordinates}");
            _chat.SendAdminAlert(actor,
                $"claimed territory {ToPrettyString(grid)} for {faction.Id} using {ToPrettyString(bannerUid)} at {bannerCoordinates:coordinates}");
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"{ToPrettyString(actor):user} claimed territory {ToPrettyString(grid):entity} for {faction.Id}");
        _chat.SendAdminAlert(actor, $"claimed territory {ToPrettyString(grid)} for {faction.Id}");
    }

    private void LogUnclaim(
        EntityUid grid,
        ProtoId<TerritoryFactionPrototype> faction,
        EntityUid? oldBanner)
    {
        if (oldBanner is { } oldBannerUid && TryComp<TransformComponent>(oldBannerUid, out var bannerXform))
        {
            var bannerCoordinates = _transform.GetMapCoordinates(bannerXform);

            _adminLog.Add(LogType.Action, LogImpact.High,
                $"Territory {ToPrettyString(grid):entity} was unclaimed from {faction.Id}; source banner {ToPrettyString(oldBannerUid):entity} at {bannerCoordinates:coordinates}");
            _chat.SendAdminAlert(
                $"Territory {ToPrettyString(grid)} was unclaimed from {faction.Id}; source banner {ToPrettyString(oldBannerUid)} at {bannerCoordinates:coordinates}");
            return;
        }

        _adminLog.Add(LogType.Action, LogImpact.High,
            $"Territory {ToPrettyString(grid):entity} was unclaimed from {faction.Id}");
        _chat.SendAdminAlert($"Territory {ToPrettyString(grid)} was unclaimed from {faction.Id}");
    }
}
