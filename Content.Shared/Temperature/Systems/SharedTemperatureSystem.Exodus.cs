// Exodus: refresh temperature slowdown when a genetic adaptation changes its thresholds.
using Content.Shared.Temperature.Components;

namespace Content.Shared.Temperature.Systems;

public sealed partial class SharedTemperatureSystem
{
    public void RefreshTemperatureSpeed(Entity<TemperatureSpeedComponent> ent, float temperature)
    {
        float? modifier = null;
        var lowestThreshold = float.PositiveInfinity;
        foreach (var (threshold, speed) in ent.Comp.Thresholds)
        {
            if (temperature < threshold && threshold < lowestThreshold)
            {
                lowestThreshold = threshold;
                modifier = speed;
            }
        }
        ent.Comp.CurrentSpeedModifier = modifier;
        ent.Comp.NextSlowdownUpdate = null;
        Dirty(ent);
        _movementSpeedModifier.RefreshMovementSpeedModifiers(ent);
    }
}
