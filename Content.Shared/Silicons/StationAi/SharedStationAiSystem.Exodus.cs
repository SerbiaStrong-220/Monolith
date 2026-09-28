// Exodus: reuse camera visibility without granting station AI interaction privileges.
using Content.Shared.StationAi;

namespace Content.Shared.Silicons.StationAi;

public abstract partial class SharedStationAiSystem
{
    public void SetVisionNetwork(Entity<StationAiVisionComponent> ent, EntityUid? network, float range, bool enabled)
    {
        if (ent.Comp.Network == network && ent.Comp.Range == range && ent.Comp.Enabled == enabled)
            return;
        ent.Comp.Network = network;
        ent.Comp.Range = range;
        ent.Comp.Enabled = enabled;
        Dirty(ent);
    }
}
