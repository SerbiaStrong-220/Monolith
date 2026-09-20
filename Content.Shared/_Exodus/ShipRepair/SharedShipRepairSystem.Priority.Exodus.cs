using Content.Shared._Exodus.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Physics;
using Content.Shared.Power.Components;
using Content.Shared.Power.Generator;
using Content.Shared.Prototypes;
using Content.Shared.SubFloor;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.ShipRepair;

public abstract partial class SharedShipRepairSystem
{
    /// <summary>Classifies an indexed snapshot entry without doing spatial queries or planning a repair.</summary>
    public ShipRepairStage GetSnapshotRepairStage(ShipRepairDataComponent data, ShipRepairEntitySpecifier spec)
    {
        if (spec.ProtoIndex < 0 || spec.ProtoIndex >= data.EntityPalette.Count ||
            !_proto.TryIndex(data.EntityPalette[spec.ProtoIndex], out var prototype))
            return ShipRepairStage.Structure;
        if (prototype.TryGetComponent<ShipRepairableComponent>(out var repair, Factory) &&
            repair.RepairTo is { } replacement && replacement.Id != prototype.ID)
        {
            if (!_proto.TryIndex(replacement, out prototype))
                return ShipRepairStage.Structure;
        }
        return GetRepairStage(prototype);
    }

    protected virtual ShipRepairStage GetRepairStage(EntityPrototype prototype)
    {
        if (prototype.TryGetComponent<ShipRepairPriorityComponent>(out var priority, Factory))
            return priority.Stage;
        // Cables, atmos piping and disposal piping share the same underfloor stage.
        if (prototype.HasComponent<SubFloorHideComponent>(Factory))
            return ShipRepairStage.Underfloor;
        if (prototype.HasComponent<DoorComponent>(Factory))
            return ShipRepairStage.Enclosure;
        if (prototype.HasComponent<FuelGeneratorComponent>(Factory) || prototype.HasComponent<BatteryComponent>(Factory))
            return ShipRepairStage.Power;
        if (GetRepairCollisionFixtures(prototype) is { } fixtures)
        {
            // Full-height barriers go last, including modded walls/windows without a particular ID or tag.
            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (fixture.Hard && (fixture.CollisionLayer & (int) CollisionGroup.FlyingMobMask) != 0)
                    return ShipRepairStage.Enclosure;
            }
        }
        return ShipRepairStage.Structure;
    }

    private static void SortRepairStages(ShipRepairPlan plan)
    {
        // Stable insertion sort preserves the support order of equal-priority floor operations.
        for (var i = 1; i < plan.Work.Count; i++)
        {
            var work = plan.Work[i];
            var j = i;
            while (j > 0 && plan.Work[j - 1].Stage > work.Stage)
            {
                plan.Work[j] = plan.Work[j - 1];
                j--;
            }
            plan.Work[j] = work;
        }
    }
}
