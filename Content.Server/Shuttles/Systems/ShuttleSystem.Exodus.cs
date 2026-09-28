// Exodus: keep the enabled flag and physical grid state consistent when releasing a forced anchor.
using Content.Server.Shuttles.Components;
using Content.Server._NF.Shuttles.Components;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    public bool TrySetEnabled(Entity<ShuttleComponent> ent, bool enabled, bool force = false)
    {
        if (!HasComp<FixturesComponent>(ent) || !HasComp<PhysicsComponent>(ent)
            || !force && HasComp<PreventGridAnchorChangesComponent>(ent))
            return false;

        ent.Comp.Enabled = enabled;
        if (enabled)
            Enable(ent, shuttle: ent.Comp, force: force);
        else
            Disable(ent, force: force);
        return true;
    }
}
