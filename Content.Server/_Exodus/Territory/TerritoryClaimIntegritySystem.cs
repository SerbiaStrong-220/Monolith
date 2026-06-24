using System;
using Content.Shared._Exodus.Territory;
using Content.Shared._Mono.ShipRepair.Components;

namespace Content.Server._Exodus.Territory;

/// <summary>
/// Checks whether a grid still has enough SRD-snapshotted repairable structure to be worth claiming.
/// </summary>
public sealed partial class TerritoryClaimIntegritySystem : EntitySystem
{
    public bool CanAnchorClaimBanner(Entity<GridTerritoryComponent> territory)
    {
        var required = Math.Clamp(territory.Comp.MinClaimRepairIntegrity, 0f, 1f);
        if (required <= 0f)
            return true;

        if (!TryComp<ShipRepairDataComponent>(territory.Owner, out var repairData))
            return false;

        var present = 0;
        var total = 0;

        foreach (var (_, chunk) in repairData.Chunks)
        {
            foreach (var (_, spec) in chunk.Entities)
            {
                total++;

                if (!TryGetEntity(spec.OriginalEntity, out var original) ||
                    original is not { } originalUid ||
                    TerminatingOrDeleted(originalUid) ||
                    !TryComp<TransformComponent>(originalUid, out var xform) ||
                    !xform.Anchored ||
                    (xform.GridUid != territory.Owner && xform.ParentUid != territory.Owner))
                {
                    continue;
                }

                present++;
            }
        }

        if (total == 0)
            return false;

        return (float)present / total >= required;
    }
}
