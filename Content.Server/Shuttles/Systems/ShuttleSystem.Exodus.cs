// Exodus: keep the enabled flag and physical grid state consistent when releasing a forced anchor.
using Content.Server.Shuttles.Components;
using Content.Server._NF.Shuttles.Components;
using Content.Shared.Shuttles.Components; // Exodus ftl-suppressor
using Content.Shared.Shuttles.Systems; // Exodus ftl-suppressor
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

    /// <summary>
    /// Exodus ftl-suppressor: cancels a jump that is still spooling up and returns the shuttle to normal flight.
    /// Does nothing once the shuttle entered hyperspace or for shuttles linked to another one's jump.
    /// </summary>
    public bool TryAbortFTLStartup(Entity<FTLComponent?, ShuttleComponent?> ent)
    {
        if (!Resolve(ent.Owner, ref ent.Comp1, ref ent.Comp2, false) ||
            ent.Comp1.State != FTLState.Starting ||
            ent.Comp1.LinkedShuttle != null)
        {
            return false;
        }

        ent.Comp1.StartupStream = _audio.Stop(ent.Comp1.StartupStream);
        _thruster.DisableLinearThrusters(ent.Comp2);
        RemComp(ent.Owner, ent.Comp1);
        _console.RefreshShuttleConsoles(ent.Owner);
        return true;
    }
}
