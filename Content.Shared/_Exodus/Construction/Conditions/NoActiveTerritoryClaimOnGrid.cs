using Content.Shared._Exodus.Territory;
using Content.Shared.Construction;
using Content.Shared.Examine;
using JetBrains.Annotations;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Shared._Exodus.Construction.Conditions;

/// <summary>
/// Construction condition used to prevent building a second active territory claim banner
/// (in yaml: `type: TerritoryBanner` which is the registered name for the TerritoryBannerComponent class)
/// on a grid that already has one.
/// 
/// This enforces the "only 1 banner" rule at construction time (in addition to runtime checks).
/// 
/// # Exodus start
/// All territory-related additions are wrapped per project style.
/// Real sprites for some factions use zaty chka placeholders for now.
/// # Exodus end
/// </summary>
[UsedImplicitly]
[DataDefinition]
public sealed partial class NoActiveTerritoryClaimOnGrid : IGraphCondition
{
    public bool Condition(EntityUid uid, IEntityManager entityManager)
    {
        if (IsClaimDisabled(uid, entityManager))
            return false;

        return !HasOtherActiveClaim(uid, entityManager, out _);
    }

    public bool DoExamine(ExaminedEvent args)
    {
        // Provide feedback in the construction examine window.
        var entMan = IoCManager.Resolve<IEntityManager>();

        if (IsClaimDisabled(args.Examined, entMan))
        {
            args.PushMarkup(Loc.GetString("construction-examine-condition-territory-claim-disabled") + "\n");
            return true;
        }

        if (HasOtherActiveClaim(args.Examined, entMan, out _))
        {
            args.PushMarkup(Loc.GetString("construction-examine-condition-territory-claim-exists") + "\n");
            return true;
        }

        return false;
    }

    public IEnumerable<ConstructionGuideEntry> GenerateGuideEntry()
    {
        yield return new ConstructionGuideEntry
        {
            Localization = "construction-step-condition-territory-no-claim"
        };
    }

    private static bool IsClaimDisabled(EntityUid uid, IEntityManager entityManager)
    {
        if (!entityManager.TryGetComponent(uid, out TransformComponent? xform))
            return false;

        if (xform.GridUid is not { } gridUid)
            return false;

        return entityManager.TryGetComponent(gridUid, out GridTerritoryComponent? territory) && !territory.Claimable;
    }

    private static bool HasOtherActiveClaim(EntityUid uid, IEntityManager entityManager, out EntityUid activeClaimBanner)
    {
        activeClaimBanner = default;

        if (!entityManager.TryGetComponent(uid, out TransformComponent? xform))
            return false;

        if (xform.GridUid is not { } gridUid)
            return false;

        if (!entityManager.TryGetComponent(gridUid, out GridTerritoryComponent? territory))
            return false;

        if (territory.ActiveClaimBanner is not { } activeBanner)
            return false;

        if (activeBanner == uid || !entityManager.EntityExists(activeBanner))
            return false;

        activeClaimBanner = activeBanner;
        return true;
    }
}
