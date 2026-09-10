using Content.Shared._Exodus.Territory;
using Content.Shared.Construction;
using Content.Shared.Construction.Conditions;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.Shared._Exodus.Construction.Conditions;

/// <summary>
/// Prevents constructing a second corporate banner on a territory grid.
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class NoActiveCompanyTerritoryBannerOnGrid : IConstructionCondition
{
    public bool Condition(EntityUid user, EntityCoordinates location, Direction direction)
    {
        var entityManager = IoCManager.Resolve<IEntityManager>();
        return !HasOtherCorporateBanner(location.EntityId, entityManager);
    }

    public ConstructionGuideEntry? GenerateGuideEntry()
    {
        return new ConstructionGuideEntry
        {
            Localization = "construction-step-condition-company-territory-no-banner"
        };
    }

    private static bool HasOtherCorporateBanner(EntityUid gridUid, IEntityManager entityManager)
    {
        if (!entityManager.TryGetComponent(gridUid, out GridTerritoryComponent? territory))
            return false;

        if (territory.ActiveCorporateBanner is not { } activeBanner ||
            !entityManager.EntityExists(activeBanner))
        {
            return false;
        }

        return entityManager.HasComponent<CompanyTerritoryBannerComponent>(activeBanner);
    }
}
