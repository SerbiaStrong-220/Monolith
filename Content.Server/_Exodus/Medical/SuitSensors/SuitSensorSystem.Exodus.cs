using Content.Server._Exodus.Medical.SuitSensors;
using Content.Shared.Medical.SuitSensor;

namespace Content.Server.Medical.SuitSensors;

public sealed partial class SuitSensorSystem
{
    private void InitializeSensorActivity()
    {
        SubscribeLocalEvent<SuitSensorComponent, ComponentStartup>(OnSensorStartup);
        SubscribeLocalEvent<SuitSensorComponent, ComponentShutdown>(OnSensorShutdown);
    }

    private void OnSensorStartup(Entity<SuitSensorComponent> ent, ref ComponentStartup args)
    {
        UpdateSensorActivity(ent);
    }

    private void OnSensorShutdown(Entity<SuitSensorComponent> ent, ref ComponentShutdown args)
    {
        RemCompDeferred<ActiveSuitSensorComponent>(ent.Owner);
    }

    private void UpdateSensorActivity(Entity<SuitSensorComponent> ent)
    {
        if (ent.Comp.LifeStage >= ComponentLifeStage.Stopping ||
            TerminatingOrDeleted(ent.Owner) ||
            ent.Comp.Mode == SuitSensorMode.SensorOff ||
            ent.Comp.User is not { } user ||
            TerminatingOrDeleted(user))
        {
            RemCompDeferred<ActiveSuitSensorComponent>(ent.Owner);
            return;
        }

        EnsureComp<ActiveSuitSensorComponent>(ent.Owner);
    }
}
