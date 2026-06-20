using Content.Shared.Actions;

namespace Content.Server._Exodus.NPC.Abilities;

public sealed class NpcDashSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcDashComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NpcDashComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnMapInit(Entity<NpcDashComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.SetUseDelay(ent.Comp.ActionEntity, ent.Comp.Cooldown);
    }

    private void OnShutdown(Entity<NpcDashComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }
}
