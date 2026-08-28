using System.Collections.Generic;
using Content.Shared._Exodus.Territory;
using Content.Shared._Mono.Company;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Territory;

public sealed partial class GridTerritorySystem
{
    /// <summary>
    /// Sets the independent corporate controller for a territory grid.
    /// </summary>
    public bool TrySetCorporateController(
        EntityUid grid,
        ProtoId<CompanyPrototype>? company,
        EntityUid? sourceBanner = null,
        EntityUid? actor = null)
    {
        if (!TryComp<GridTerritoryComponent>(grid, out var territory) ||
            company is not null && !territory.Claimable)
        {
            return false;
        }

        var oldCompany = territory.CorporateController;
        var oldSourceBanner = territory.ActiveCorporateBanner;
        var controllerChanged =
            !EqualityComparer<ProtoId<CompanyPrototype>?>.Default.Equals(oldCompany, company) ||
            oldSourceBanner != sourceBanner;

        territory.CorporateController = company;
        territory.ActiveCorporateBanner = sourceBanner;
        Dirty(grid, territory);

        if (!controllerChanged)
            return true;

        var ev = new GridTerritoryCorporateControllerChangedEvent(
            grid,
            oldCompany,
            company,
            oldSourceBanner,
            sourceBanner,
            actor);
        RaiseLocalEvent(grid, ref ev, true);
        return true;
    }

    /// <summary>
    /// Clears the independent corporate controller for a territory grid.
    /// </summary>
    public bool ClearCorporateController(EntityUid grid, EntityUid? actor = null)
    {
        return TrySetCorporateController(grid, null, null, actor);
    }
}
