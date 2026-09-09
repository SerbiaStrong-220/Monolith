// Exodus-begin
using Content.Server._Exodus.Ghost.Roles;
using Content.Server.Ghost.Roles.Components;
using Content.Shared.Mind.Components;

namespace Content.Server.Ghost.Roles;

public sealed partial class GhostRoleSystem
{
    /// <summary>
    /// Reopens an abandoned takeover role unless another system still reserves it.
    /// </summary>
    public bool TryReregisterGhostRole(Entity<GhostRoleComponent> ent)
    {
        if (Deleted(ent)
            || MetaData(ent).EntityLifeStage >= EntityLifeStage.Terminating
            || !ent.Comp.ReregisterOnGhost
            || ent.Comp.LifeStage > ComponentLifeStage.Running
            || !TryComp<GhostTakeoverAvailableComponent>(ent, out var takeover)
            || takeover.LifeStage > ComponentLifeStage.Running
            || TryComp<MindContainerComponent>(ent, out var container) && container.HasMind)
        {
            return false;
        }

        var attempt = new GhostRoleReregisterAttemptEvent();
        RaiseLocalEvent(ent, ref attempt);
        if (attempt.Cancelled)
            return false;

        ent.Comp.Taken = false;
        RegisterGhostRole(ent);
        return true;
    }
}
// Exodus-end
