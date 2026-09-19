using Content.Shared._Exodus.ShipRepair;
using Content.Shared.Power.Components;
using Content.Shared.Power.Generator;
using Content.Shared.Prototypes;
using Content.Shared.SubFloor;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.ShipRepair;

public abstract partial class SharedShipRepairSystem
{
    protected virtual ShipRepairStage GetRepairStage(EntityPrototype prototype)
    {
        if (prototype.TryGetComponent<ShipRepairPriorityComponent>(out var priority, Factory))
            return priority.Stage;
        // Cables, atmos piping and disposal piping share the same underfloor stage.
        if (prototype.HasComponent<SubFloorHideComponent>(Factory))
            return ShipRepairStage.Underfloor;
        if (prototype.HasComponent<FuelGeneratorComponent>(Factory) || prototype.HasComponent<BatteryComponent>(Factory))
            return ShipRepairStage.Power;
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
