using Content.Server.Medical.SuitSensors;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;

namespace Content.Server._Exodus.MedicalTracking;

public sealed partial class MedicalTrackingSystem
{
    [Dependency] private InventorySystem _inventory = default!;

    private EntityQuery<InventoryComponent> _inventoryQuery;
    private EntityQuery<MobStateComponent> _mobStateQuery;
    private EntityQuery<SuitSensorComponent> _sensorQuery;

    private void InitializeVisibility()
    {
        _inventoryQuery = GetEntityQuery<InventoryComponent>();
        _mobStateQuery = GetEntityQuery<MobStateComponent>();
        _sensorQuery = GetEntityQuery<SuitSensorComponent>();
    }

    private bool CanTrackBody(EntityUid uid)
    {
        return _mobStateQuery.TryComp(uid, out var state) && CanTrackBody((uid, state));
    }

    private bool CanTrackBody(Entity<MobStateComponent> body)
    {
        if (TerminatingOrDeleted(body) || !MetaData(body).EntityInitialized)
            return false;

        // Emergency tracking remains available without clothing or working suit sensors.
        if (body.Comp.CurrentState is MobState.Critical or MobState.Dead)
            return true;

        if (body.Comp.CurrentState != MobState.Alive || !_inventoryQuery.TryComp(body, out var inventory))
            return false;

        var slots = _inventory.GetSlotEnumerator((body.Owner, inventory), SlotFlags.WITHOUT_POCKET);
        while (slots.NextItem(out var item, out var slot))
        {
            if (_sensorQuery.TryComp(item, out var sensor) && sensor.LifeStage < ComponentLifeStage.Stopping &&
                sensor.User == body.Owner && sensor.ActivationSlot == slot.Name &&
                sensor.Mode == SuitSensorMode.SensorCords)
                return true;
        }

        return false;
    }

    private bool CanTrackBrain(Entity<MedicalTrackingBrainComponent> brain)
    {
        if (!brain.Comp.Registered || TerminatingOrDeleted(brain) || !MetaData(brain).EntityInitialized)
            return false;

        // Use the current body after transplantation, not the implant's original owner.
        // Detached brains retain emergency tracking after extraction or body destruction.
        return !_organQuery.TryComp(brain, out var organ) || organ.Body is not { } body || CanTrackBody(body);
    }
}
