// Exodus: change engine availability through the native thrust bookkeeping.
using Content.Server.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ThrusterSystem
{
    public void SetEnabled(Entity<ThrusterComponent> ent, bool enabled)
    {
        if (ent.Comp.Enabled == enabled)
            return;
        ent.Comp.Enabled = enabled;
        if (enabled && CanEnable(ent, ent.Comp))
            EnableThruster(ent, ent.Comp);
        else if (ent.Comp.IsOn)
            DisableThruster(ent, ent.Comp);
        Dirty(ent);
    }
}
