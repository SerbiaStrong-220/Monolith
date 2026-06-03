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
        if (!entityManager.TryGetComponent(uid, out TransformComponent? xform))
            return true;

        var gridUid = xform.GridUid;
        if (gridUid == null)
            return true;

        // Look for any other anchored banner that carries a territory claim on the same grid.
        var query = entityManager.EntityQueryEnumerator<TerritoryBannerComponent, TransformComponent>();
        while (query.MoveNext(out var otherUid, out _, out var otherXform))
        {
            if (otherUid == uid)
                continue;

            if (otherXform.GridUid != gridUid)
                continue;

            if (otherXform.Anchored)
                return false; // already a claim banner on this grid
        }

        return true;
    }

    public bool DoExamine(ExaminedEvent args)
    {
        // Provide feedback in the construction examine window.
        var entMan = IoCManager.Resolve<IEntityManager>();

        if (!entMan.TryGetComponent(args.Examined, out TransformComponent? xform))
            return false;

        var gridUid = xform.GridUid;
        if (gridUid == null)
            return false;

        var query = entMan.EntityQueryEnumerator<TerritoryBannerComponent, TransformComponent>();
        while (query.MoveNext(out var otherUid, out _, out var otherXform))
        {
            if (otherUid == args.Examined)
                continue;

            if (otherXform.GridUid != gridUid || !otherXform.Anchored)
                continue;

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
}
