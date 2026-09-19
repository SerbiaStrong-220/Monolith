using Content.Server.Power.Components;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.ShipRepair;

public sealed partial class ShipRepairSystem
{
    protected override ShipRepairStage GetRepairStage(EntityPrototype prototype)
    {
        var stage = base.GetRepairStage(prototype);
        if (stage != ShipRepairStage.Structure || prototype.HasComponent<ShipRepairPriorityComponent>(Factory))
            return stage;
        if (prototype.HasComponent<PowerSupplierComponent>(Factory) ||
            prototype.HasComponent<PowerNetworkBatteryComponent>(Factory) ||
            prototype.HasComponent<ApcPowerProviderComponent>(Factory))
            return ShipRepairStage.Power;
        return stage;
    }
}
