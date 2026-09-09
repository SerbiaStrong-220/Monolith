using Content.Shared._Exodus.Territory;
using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Shuttles;

/// <summary>
/// Resolves presentation from networked data on demand. No ownership changes or per-frame entity scans.
/// </summary>
public sealed partial class IffAffiliationSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototype = default!;

    private EntityQuery<IffAffiliationComponent> _affiliationQuery;
    private EntityQuery<GridTerritoryComponent> _territoryQuery;

    public override void Initialize()
    {
        base.Initialize();
        _affiliationQuery = GetEntityQuery<IffAffiliationComponent>();
        _territoryQuery = GetEntityQuery<GridTerritoryComponent>();
    }

    /// <summary>
    /// Managed labels must not be recolored by the upstream company renderer.
    /// Unconfigured purchased ships retain their existing company display.
    /// </summary>
    public bool UsesFactionColor(EntityUid grid)
    {
        return _affiliationQuery.HasComponent(grid) || _territoryQuery.HasComponent(grid);
    }

    /// <summary>
    /// The FTL map only shows affiliation for actual corporate control, never status placeholders,
    /// military affiliation or a fixed organization. Explicitly disabled labels stay hidden.
    /// </summary>
    public bool HasCorporateControlLabel(EntityUid grid)
    {
        if (_affiliationQuery.TryGetComponent(grid, out var affiliation) &&
            affiliation.Mode != IffAffiliationMode.CorporateControl)
        {
            return false;
        }

        return _territoryQuery.TryGetComponent(grid, out var territory) &&
               territory.Radius > 0f && territory.Claimable &&
               territory.ControllingFaction != null &&
               territory.CorporateController is { } company && company != "None" &&
               _prototype.HasIndex(company);
    }

    /// <summary>
    /// Returns true for managed grids. An empty label intentionally suppresses the second IFF line.
    /// </summary>
    public bool TryGetLabel(EntityUid grid, out string label)
    {
        label = string.Empty;
        _affiliationQuery.TryGetComponent(grid, out var affiliation);
        _territoryQuery.TryGetComponent(grid, out var territory);

        if (affiliation == null && territory == null)
            return false;

        if (affiliation?.Mode != IffAffiliationMode.None &&
            territory is { Claimable: true, ControllingFaction: { } controller } &&
            _prototype.TryIndex(controller, out var controllerPrototype) &&
            controllerPrototype.ControlLabel is { } controlLabel)
        {
            label = Loc.GetString(controlLabel);
            return true;
        }

        switch (affiliation?.Mode ?? IffAffiliationMode.CorporateControl)
        {
            case IffAffiliationMode.None:
                return true;

            case IffAffiliationMode.FixedLabel:
                label = affiliation?.Label is { } labelId ? Loc.GetString(labelId) : string.Empty;
                return true;

            case IffAffiliationMode.Faction:
                var factionId = territory is { Radius: > 0f, ColorPoiByFaction: true }
                    ? territory.ControllingFaction
                    : affiliation?.Faction;
                label = factionId is { } id && _prototype.TryIndex(id, out var faction)
                    ? Loc.GetString(faction.DisplayName ?? faction.RadarLabel)
                    : Loc.GetString("exodus-iff-no-faction-control");
                return true;

            case IffAffiliationMode.FixedCompany:
                label = GetCompanyLabel(affiliation?.Company);
                return true;

            default:
                // Non-capturable POIs do not have either layer of capture status to display.
                if (territory is not { Radius: > 0f, Claimable: true })
                    return true;

                // A corporate flag cannot establish faction control on its own.
                label = territory.ControllingFaction == null
                    ? Loc.GetString("exodus-iff-no-faction-control")
                    : GetCompanyLabel(territory.CorporateController);
                return true;
        }
    }

    /// <summary>
    /// Formats an optional territory status without persisting it in the station's actual name.
    /// </summary>
    public string GetGridName(EntityUid grid, string name)
    {
        if (_territoryQuery.TryGetComponent(grid, out var territory) &&
            territory is { Claimable: true, ControllingFaction: { } controller } &&
            _prototype.TryIndex(controller, out var faction) &&
            faction.IffStatus is { } status)
        {
            var displayName = string.IsNullOrEmpty(name) ? Loc.GetString("shuttle-console-unknown") : name;
            return Loc.GetString("exodus-iff-territory-status", ("name", displayName), ("status", Loc.GetString(status)));
        }

        return name;
    }

    /// <summary>
    /// Applies display affiliation to a summoned grid without granting faction ownership or access.
    /// </summary>
    public void SetFaction(Entity<IffAffiliationComponent> ent, ProtoId<TerritoryFactionPrototype> faction)
    {
        ent.Comp.Mode = IffAffiliationMode.Faction;
        ent.Comp.Faction = faction;
        ent.Comp.Company = null;
        Dirty(ent);
    }

    public bool TryGetColor(EntityUid grid, out Color color)
    {
        color = default;

        // A real capture, or its removal, takes precedence over the configured display faction.
        if (_territoryQuery.TryGetComponent(grid, out var territory) &&
            territory.Radius > 0f && territory.ColorPoiByFaction)
        {
            color = territory.ControllingFaction is { } id && _prototype.TryIndex(id, out var faction)
                ? faction.Color
                : territory.NeutralPoiColor;
            return true;
        }

        if (_affiliationQuery.TryGetComponent(grid, out var affiliation) &&
            affiliation.Faction is { } displayFactionId &&
            _prototype.TryIndex(displayFactionId, out var displayFaction))
        {
            color = displayFaction.Color;
            return true;
        }

        // Service hubs and POIs without a faction keep their configured IFF color, never a company color.
        return false;
    }

    private string GetCompanyLabel(ProtoId<CompanyPrototype>? companyId)
    {
        if (companyId is { } id && id != "None" && _prototype.TryIndex(id, out var company))
            return Loc.GetString(company.Name);

        return Loc.GetString("exodus-iff-no-corporate-control");
    }
}
