using Content.Server.Popups;
using Content.Shared._Exodus.Territory;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Territory;

public sealed partial class GridTerritorySystem
{
    [Dependency] private IGameTiming _captureTiming = default!;
    [Dependency] private PopupSystem _capturePopup = default!;

    private EntityQuery<TerritoryBannerComponent> _captureBannerQuery;
    private EntityQuery<TransformComponent> _captureTransformQuery;

    private void InitializeCapture()
    {
        _captureBannerQuery = GetEntityQuery<TerritoryBannerComponent>();
        _captureTransformQuery = GetEntityQuery<TransformComponent>();
    }

    /// <summary>Reserves the grid for a banner without granting faction ownership or its benefits.</summary>
    public bool TryStartCapture(Entity<GridTerritoryComponent> territory, Entity<TerritoryBannerComponent> banner, EntityUid? actor)
    {
        if (!territory.Comp.Claimable || territory.Comp.ActiveClaimBanner != null ||
            TerminatingOrDeleted(territory) || TerminatingOrDeleted(banner) ||
            !territory.Comp.Initialized || !banner.Comp.Initialized ||
            EntityManager.IsQueuedForDeletion(territory) || EntityManager.IsQueuedForDeletion(banner) ||
            !_captureTransformQuery.TryGetComponent(banner, out var xform) || !xform.Anchored ||
            xform.GridUid != territory.Owner || !_claimRules.CanStartClaim(banner.Comp.Faction, out _))
        {
            return false;
        }

        var duration = _claimRules.GetClaimDuration(banner.Comp.Faction);
        if (duration <= TimeSpan.Zero)
            return false;

        // ActiveClaimBanner also reserves the existing construction and hive-core claim checks.
        SetController(territory, null, banner, actor);
        var capture = EnsureComp<TerritoryCaptureComponent>(territory);
        capture.Faction = banner.Comp.Faction;
        capture.Banner = banner.Owner;
        capture.Actor = actor;
        capture.EndsAt = _captureTiming.CurTime + duration;
        capture.Color = _claimRules.GetContestedColor();
        capture.PublishedEndsAt = capture.EndsAt;
        Dirty(territory, capture);
        EnsureVisual(territory);

        var ev = new GridTerritoryCaptureStartedEvent(territory.Owner, banner.Comp.Faction, banner.Owner, actor, duration);
        RaiseLocalEvent(territory.Owner, ref ev, true);
        return true;
    }

    private void ClearCaptureState(EntityUid grid)
    {
        if (!TryComp<TerritoryCaptureComponent>(grid, out var capture) || capture.Faction == null)
            return;

        capture.Faction = null;
        capture.Banner = null;
        capture.Actor = null;
        Dirty(grid, capture);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _captureTiming.CurTime;
        var query = EntityQueryEnumerator<TerritoryCaptureComponent, GridTerritoryComponent>();
        while (query.MoveNext(out var grid, out var capture, out var territory))
        {
            if (capture.Faction is not { } faction)
                continue;

            if (TerminatingOrDeleted(grid) || EntityManager.IsQueuedForDeletion(grid))
            {
                ClearCaptureState(grid);
                continue;
            }

            if (capture.PublishedEndsAt != capture.EndsAt)
            {
                // AutoPause compensates the networked deadline. Refresh radar data for viewers outside the grid's PVS.
                capture.PublishedEndsAt = capture.EndsAt;
                EnsureVisual((grid, territory));
            }

            if (now < capture.EndsAt)
                continue;

            // Integrity deliberately isn't checked again: only the flag must survive its full countdown.
            if (!territory.Claimable || territory.ControllingFaction != null ||
                capture.Banner is not { } banner || territory.ActiveClaimBanner != banner ||
                TerminatingOrDeleted(banner) || EntityManager.IsQueuedForDeletion(banner) ||
                !_captureBannerQuery.TryGetComponent(banner, out var bannerComp) || bannerComp.Faction != faction ||
                !_captureTransformQuery.TryGetComponent(banner, out var xform) || !xform.Anchored || xform.GridUid != grid)
            {
                ClearController(grid);
                continue;
            }

            var actor = capture.Actor;
            if (actor is { } actorUid && TerminatingOrDeleted(actorUid))
                actor = null;

            SetController(grid, faction, banner, actor);
            if (territory.ControllingFaction != faction || territory.ActiveClaimBanner != banner)
                continue;

            _claimRules.RecordSuccessfulClaim(faction);
            _capturePopup.PopupEntity(Loc.GetString("grid-territory-claimed"), banner);
        }
    }
}
