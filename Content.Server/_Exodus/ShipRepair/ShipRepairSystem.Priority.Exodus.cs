using Content.Server.Power.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Shared._Exodus.ShipRepair;
using Content.Shared.Doors.Components;
using Content.Shared.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.ShipRepair;

public sealed partial class ShipRepairSystem
{
    protected override ShipRepairStage GetRepairStage(EntityPrototype prototype)
    {
        var stage = base.GetRepairStage(prototype);
        if (!prototype.HasComponent<ShipRepairPriorityComponent>(Factory) &&
            (prototype.HasComponent<GasVentPumpComponent>(Factory) ||
             prototype.HasComponent<GasVentScrubberComponent>(Factory) ||
             prototype.HasComponent<GasPassiveVentComponent>(Factory)))
            return ShipRepairStage.Underfloor;
        if (stage < ShipRepairStage.Structure || prototype.HasComponent<ShipRepairPriorityComponent>(Factory) ||
            prototype.HasComponent<DoorComponent>(Factory))
            return stage;
        if (prototype.HasComponent<PowerSupplierComponent>(Factory) ||
            prototype.HasComponent<PowerNetworkBatteryComponent>(Factory) ||
            prototype.HasComponent<ApcPowerProviderComponent>(Factory) ||
            prototype.HasComponent<ApcPowerReceiverComponent>(Factory) ||
            prototype.HasComponent<PowerConsumerComponent>(Factory))
            return ShipRepairStage.Power;
        return stage;
    }
}
