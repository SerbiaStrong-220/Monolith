using Content.Server._Mono.Radar;
using Content.Server.Popups;
using Content.Shared._Exodus.Territory;
using Content.Shared._Mono.Company;
using Content.Shared._Mono.Radar;
using Content.Shared.Access.Systems;
using Content.Shared.Construction.Components;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Maps;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Handles corporate banners as an independent layer of territory control.
/// A corporation may expand only inside the faction territory selected by its first active banner.
/// </summary>
public sealed class CompanyTerritoryBannerSystem : EntitySystem
{
    private const float ActiveCompanyBannerRadarBlipHalfSize = 1.5f;
    private const float ActiveCompanyBannerRadarEdgeVisibilityPadding = 10_000f;

    [Dependency] private readonly SharedIdCardSystem _idCard = default!;
    [Dependency] private readonly GridTerritorySystem _territory = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CompanyTerritoryBannerComponent, AnchorAttemptEvent>(OnAnchorAttempt);
        SubscribeLocalEvent<CompanyTerritoryBannerComponent, BeforeAnchoredEvent>(OnBeforeAnchored);
        SubscribeLocalEvent<CompanyTerritoryBannerComponent, UserAnchoredEvent>(OnUserAnchored);
        SubscribeLocalEvent<CompanyTerritoryBannerComponent, AnchorStateChangedEvent>(OnAnchorChanged);
        SubscribeLocalEvent<CompanyTerritoryBannerComponent, EntParentChangedMessage>(OnParentChanged);
        SubscribeLocalEvent<CompanyTerritoryBannerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<CompanyTerritoryBannerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<CompanyTerritoryBannerComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<GridTerritoryControllerChangedEvent>(OnTerritoryControllerChanged);
    }

    private void OnExamined(Entity<CompanyTerritoryBannerComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Company is not { } company)
        {
            args.PushMarkup(Loc.GetString("company-territory-banner-examine-unassigned") + "\n");
            return;
        }

        args.PushMarkup(
            Loc.GetString(
                "company-territory-banner-examine-company",
                ("company", GetCompanyName(company))) + "\n");

        if (ent.Comp.TerritoryFaction is { } faction)
        {
            args.PushMarkup(
                Loc.GetString(
                    "company-territory-banner-examine-faction",
                    ("faction", GetFactionName(faction))) + "\n");
        }
    }

    private void OnStartup(Entity<CompanyTerritoryBannerComponent> ent, ref ComponentStartup args)
    {
        if (Transform(ent).Anchored)
            TryClaim(ent, false);
    }

    private void OnTerritoryControllerChanged(ref GridTerritoryControllerChangedEvent args)
    {
        if (!TryComp<GridTerritoryComponent>(args.Grid, out var territory))
        {
            return;
        }

        if (ShouldClearCorporateController((args.Grid, territory), args.NewFaction))
            _territory.ClearCorporateController(args.Grid, args.Actor);

        if (args.NewFaction is null)
            return;

        TryClaimFromAnchoredBannersOnGrid((args.Grid, territory));
    }

    private bool ShouldClearCorporateController(
        Entity<GridTerritoryComponent> territory,
        ProtoId<TerritoryFactionPrototype>? newFaction)
    {
        if (territory.Comp.ActiveCorporateBanner is not { } activeBanner)
            return false;

        if (newFaction is null)
        {
            ClearActiveBannerBlip(activeBanner);
            return true;
        }

        if (!TryComp<CompanyTerritoryBannerComponent>(activeBanner, out var banner) ||
            banner.TerritoryFaction != newFaction)
        {
            ClearActiveBannerBlip(activeBanner);
            return true;
        }

        return false;
    }

    internal void TryClaimFromAnchoredBannersOnGrid(Entity<GridTerritoryComponent> territory)
    {
        if (!TryComp<MapGridComponent>(territory.Owner, out var grid))
            return;

        foreach (var uid in _map.GetLocalAnchoredEntities(territory.Owner, grid, grid.LocalAABB))
        {
            if (!TryComp<CompanyTerritoryBannerComponent>(uid, out var banner))
                continue;

            TryClaim((uid, banner), false);
        }
    }

    private void OnAnchorAttempt(Entity<CompanyTerritoryBannerComponent> ent, ref AnchorAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryResolveTerritory(ent.Owner, out var grid, out var territory))
        {
            DenyAnchor(ent.Owner, args.User, "company-territory-banner-no-territory", args);
            return;
        }

        if (!territory.Claimable)
        {
            DenyAnchor(ent.Owner, args.User, "company-territory-banner-disabled", args);
            return;
        }

        if (territory.ControllingFaction is not { } faction)
        {
            DenyAnchor(ent.Owner, args.User, "company-territory-banner-neutral", args);
            return;
        }

        if (territory.ActiveCorporateBanner is { } activeBanner && activeBanner != ent.Owner)
        {
            if (Exists(activeBanner) && HasComp<CompanyTerritoryBannerComponent>(activeBanner))
            {
                DenyAnchor(ent.Owner, args.User, "company-territory-banner-already-claimed", args);
                return;
            }

            _territory.ClearCorporateController(grid);
        }

        if (!TryGetUserCompany(args.User, out var company))
        {
            DenyAnchor(ent.Owner, args.User, "company-territory-banner-no-card", args);
            return;
        }

        if (ent.Comp.Company is { } bannerCompany && bannerCompany != company)
        {
            DenyAnchor(ent.Owner, args.User, "company-territory-banner-company-mismatch", args);
            return;
        }

        if (TryGetBoundFaction(company, out var boundFaction) && boundFaction != faction)
        {
            _popup.PopupEntity(
                Loc.GetString(
                    "company-territory-banner-wrong-faction",
                    ("faction", GetFactionName(boundFaction))),
                ent.Owner,
                args.User);
            args.Cancel();
        }
    }

    private void OnBeforeAnchored(Entity<CompanyTerritoryBannerComponent> ent, ref BeforeAnchoredEvent args)
    {
        var pendingActor = EnsureComp<PendingTerritoryClaimActorComponent>(ent.Owner);
        pendingActor.Actor = args.User;
    }

    private void OnAnchorChanged(Entity<CompanyTerritoryBannerComponent> ent, ref AnchorStateChangedEvent args)
    {
        if (args.Anchored)
        {
            EntityUid? actor = null;
            if (TryComp<PendingTerritoryClaimActorComponent>(ent.Owner, out var pendingActor))
            {
                actor = pendingActor.Actor;
                ClearPendingClaimActor(ent.Owner, pendingActor);
            }

            TryClaim(ent, actor is not null, actor);
        }
        else
        {
            ClearPendingClaimActor(ent.Owner);
            TryUnclaim(ent);
        }
    }

    private void OnUserAnchored(Entity<CompanyTerritoryBannerComponent> ent, ref UserAnchoredEvent args)
    {
        ClearPendingClaimActor(ent.Owner);
    }

    private void OnParentChanged(Entity<CompanyTerritoryBannerComponent> ent, ref EntParentChangedMessage args)
    {
        if (args.OldParent is { } oldParent && oldParent != args.Transform.GridUid)
            TryUnclaimFromGrid(ent, oldParent);

        if (args.Transform.Anchored)
            TryClaim(ent);
    }

    private void OnShutdown(Entity<CompanyTerritoryBannerComponent> ent, ref ComponentShutdown args)
    {
        ClearActiveBannerBlip(ent.Owner);
        TryUnclaim(ent);
    }

    private void TryClaim(
        Entity<CompanyTerritoryBannerComponent> banner,
        bool showPopup = true,
        EntityUid? actor = null)
    {
        if (!TryResolveTerritory(banner.Owner, out var grid, out var territory))
            return;

        if (!territory.Claimable)
        {
            if (showPopup)
                _popup.PopupEntity(Loc.GetString("company-territory-banner-disabled"), banner.Owner);

            return;
        }

        var currentFaction = territory.ControllingFaction;
        if (actor is not null && currentFaction is null)
            return;

        if (territory.ActiveCorporateBanner is { } activeBanner && activeBanner != banner.Owner)
        {
            if (Exists(activeBanner) && HasComp<CompanyTerritoryBannerComponent>(activeBanner))
            {
                if (showPopup)
                    _popup.PopupEntity(Loc.GetString("company-territory-banner-already-claimed"), banner.Owner);

                return;
            }

            _territory.ClearCorporateController(grid);
        }

        if (territory.ActiveCorporateBanner == banner.Owner)
        {
            if (banner.Comp.Company is { } activeCompany &&
                banner.Comp.TerritoryFaction is { } bannerFaction &&
                currentFaction == bannerFaction)
            {
                if (_territory.TrySetCorporateController(grid, activeCompany, banner.Owner, actor))
                    ConfigureActiveBannerBlip(banner, (grid, territory));
            }
            else
            {
                ClearActiveBannerBlip(banner.Owner);
                _territory.ClearCorporateController(grid);
            }

            return;
        }

        if (banner.Comp.Company is not { } company)
        {
            if (actor is not { } actorUid || !TryGetUserCompany(actorUid, out company))
                return;

            banner.Comp.Company = company;
        }

        var territoryFaction = currentFaction;
        if (actor is null && banner.Comp.TerritoryFaction is { } storedFaction)
        {
            if (currentFaction != storedFaction)
                return;

            territoryFaction = storedFaction;
        }

        if (territoryFaction is not { } claimFaction)
            return;

        if (actor is not null && TryGetBoundFaction(company, out var boundFaction) && boundFaction != claimFaction)
        {
            if (showPopup)
            {
                _popup.PopupEntity(
                    Loc.GetString(
                        "company-territory-banner-wrong-faction",
                        ("faction", GetFactionName(boundFaction))),
                    banner.Owner,
                    actor.Value);
            }

            return;
        }

        if (TryGetBoundFaction(company, out var existingFaction) && existingFaction != claimFaction)
            return;

        banner.Comp.TerritoryFaction = claimFaction;
        Dirty(banner.Owner, banner.Comp);

        if (!_territory.TrySetCorporateController(grid, company, banner.Owner, actor))
            return;

        ConfigureActiveBannerBlip(banner, (grid, territory));

        if (showPopup)
        {
            if (actor is { } actorUid)
                _popup.PopupEntity(Loc.GetString("company-territory-banner-claimed"), banner.Owner, actorUid);
            else
                _popup.PopupEntity(Loc.GetString("company-territory-banner-claimed"), banner.Owner);
        }
    }

    private void TryUnclaim(Entity<CompanyTerritoryBannerComponent> banner)
    {
        var xform = Transform(banner);
        if (!TryResolveTerritoryGrid(xform, out var grid))
            return;

        TryUnclaimFromGrid(banner, grid);
    }

    private void TryUnclaimFromGrid(Entity<CompanyTerritoryBannerComponent> banner, EntityUid grid)
    {
        if (!TryComp<GridTerritoryComponent>(grid, out var territory) ||
            territory.ActiveCorporateBanner != banner.Owner)
        {
            return;
        }

        ClearActiveBannerBlip(banner.Owner);
        _territory.ClearCorporateController(grid);
        _popup.PopupEntity(Loc.GetString("company-territory-banner-unclaimed"), banner.Owner);
    }

    private void ConfigureActiveBannerBlip(
        Entity<CompanyTerritoryBannerComponent> banner,
        Entity<GridTerritoryComponent> territory)
    {
        EnsureComp<PhysicsComponent>(banner);

        var marker = EnsureComp<ActiveTerritoryBannerRadarBlipComponent>(banner);
        marker.Grid = territory.Owner;
        marker.Removing = false;

        var blip = EnsureComp<RadarBlipComponent>(banner);
        blip.Enabled = true;
        blip.RequireNoGrid = false;
        blip.VisibleFromOtherGrids = true;
        blip.MaxDistance = territory.Comp.Radius + ActiveCompanyBannerRadarEdgeVisibilityPadding;
        blip.GridConfig = null;
        blip.Config = new BlipConfig
        {
            Bounds = new Box2(
                -ActiveCompanyBannerRadarBlipHalfSize,
                -ActiveCompanyBannerRadarBlipHalfSize,
                ActiveCompanyBannerRadarBlipHalfSize,
                ActiveCompanyBannerRadarBlipHalfSize),
            Color = Color.White,
            Shape = RadarBlipShape.Square,
            RespectZoom = true,
            Rotate = false,
        };
    }

    private void ClearActiveBannerBlip(EntityUid banner)
    {
        if (!TryComp<ActiveTerritoryBannerRadarBlipComponent>(banner, out var marker) ||
            marker.Removing)
        {
            return;
        }

        marker.Removing = true;
        RemCompDeferred<ActiveTerritoryBannerRadarBlipComponent>(banner);

        if (HasComp<RadarBlipComponent>(banner))
            RemCompDeferred<RadarBlipComponent>(banner);
    }

    private bool TryGetBoundFaction(
        ProtoId<CompanyPrototype> company,
        out ProtoId<TerritoryFactionPrototype> faction)
    {
        var query = EntityManager.AllEntityQueryEnumerator<GridTerritoryComponent>();
        while (query.MoveNext(out _, out var territory))
        {
            if (territory.CorporateController != company ||
                territory.ActiveCorporateBanner is not { } activeBanner ||
                territory.ControllingFaction is not { } controllingFaction ||
                !TryComp<CompanyTerritoryBannerComponent>(activeBanner, out var banner) ||
                banner.Company != company ||
                banner.TerritoryFaction != controllingFaction)
            {
                continue;
            }

            faction = controllingFaction;
            return true;
        }

        faction = default;
        return false;
    }

    private bool TryGetUserCompany(EntityUid user, out ProtoId<CompanyPrototype> company)
    {
        if (_idCard.TryFindIdCard(user, out var idCard) &&
            idCard.Comp.CompanyName.Id != "None")
        {
            company = idCard.Comp.CompanyName;
            return true;
        }

        company = default;
        return false;
    }

    private string GetCompanyName(ProtoId<CompanyPrototype> company)
    {
        return _prototypes.TryIndex(company, out var companyPrototype)
            ? Loc.GetString(companyPrototype.Name)
            : company.Id;
    }

    private string GetFactionName(ProtoId<TerritoryFactionPrototype> faction)
    {
        return _prototypes.TryIndex(faction, out var factionPrototype)
            ? Loc.GetString(factionPrototype.RadarLabel)
            : faction.Id;
    }

    private void DenyAnchor(
        EntityUid banner,
        EntityUid user,
        string message,
        AnchorAttemptEvent args)
    {
        _popup.PopupEntity(Loc.GetString(message), banner, user);
        args.Cancel();
    }

    private bool TryResolveTerritory(
        EntityUid banner,
        out EntityUid grid,
        out GridTerritoryComponent territory)
    {
        grid = default;
        territory = default!;

        if (!TryResolveTerritoryGrid(Transform(banner), out grid))
            return false;

        if (!TryComp<GridTerritoryComponent>(grid, out var resolvedTerritory))
            return false;

        territory = resolvedTerritory;
        return true;
    }

    private bool TryResolveTerritoryGrid(TransformComponent xform, out EntityUid grid)
    {
        if (xform.GridUid is { } gridUid)
        {
            grid = gridUid;
            return true;
        }

        if (HasComp<MapGridComponent>(xform.ParentUid))
        {
            grid = xform.ParentUid;
            return true;
        }

        grid = default;
        return false;
    }

    private void ClearPendingClaimActor(EntityUid banner, PendingTerritoryClaimActorComponent? pendingActor = null)
    {
        if (!Resolve(banner, ref pendingActor, false) ||
            pendingActor.LifeStage >= ComponentLifeStage.Stopping)
        {
            return;
        }

        RemCompDeferred<PendingTerritoryClaimActorComponent>(banner);
    }
}
