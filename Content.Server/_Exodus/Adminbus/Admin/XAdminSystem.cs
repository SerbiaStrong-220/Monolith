using Content.Server.Administration.Logs;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Adminbus.Admin;

public sealed partial class XAdminSystem : EntitySystem
{
    [Dependency] private readonly IAdminLogManager _adminLog = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActorComponent, EntParentChangedMessage>(OnLogParentChange);
    }

    private void OnLogParentChange(Entity<ActorComponent> ent, ref EntParentChangedMessage ev)
    {
        if (HasComp<GhostComponent>(ent)) // omit ghosts from logs
            return;

        if (ev.OldMapId != ev.Transform.MapUid) // if it's only map change, for example, shuttle FTL, omit it
            return;

        var oldParent = ev.OldParent?.IsValid() ?? false ? ToPrettyString(ev.OldParent) : "space";
        var newParent = ev.Entity.IsValid() ? ToPrettyString(ev.Entity) : "space";

        _adminLog.Add(LogType.Movement, LogImpact.Low, $"{ToPrettyString(ent)} changed parent from {oldParent} to {newParent}");
    }
}
