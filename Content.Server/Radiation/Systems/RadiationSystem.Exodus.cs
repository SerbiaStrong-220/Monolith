// SS220 / Exodus: update disease radiation sources through the radiation system.
using Content.Shared.Radiation.Components;

namespace Content.Server.Radiation.Systems;

public sealed partial class RadiationSystem
{
    public void SetIntensity(Entity<RadiationSourceComponent?> entity, float intensity)
    {
        if (!Resolve(entity, ref entity.Comp, false))
            return;

        entity.Comp.Intensity = Math.Max(0f, intensity);
    }
}
