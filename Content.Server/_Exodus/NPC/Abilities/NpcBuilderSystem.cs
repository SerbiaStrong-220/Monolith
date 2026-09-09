using Content.Server.NPC.Systems;
using Content.Shared._Exodus.NPC.Abilities;
using Content.Shared.Actions;

namespace Content.Server._Exodus.NPC.Abilities;

public sealed partial class NpcBuilderSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private NPCSystem _npc = default!;

    private const string BuildCastTimeKey = "BuildCastTime";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NpcBuilderComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<NpcBuilderComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<NpcBuilderComponent, NpcBuildActionEvent>(OnBuild);
    }

    private void OnMapInit(Entity<NpcBuilderComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ActionEntity, ent.Comp.Action);
        _actions.SetUseDelay(ent.Comp.ActionEntity, ent.Comp.Cooldown);
        _npc.SetBlackboard(ent, BuildCastTimeKey, (float) ent.Comp.BuildTime.TotalSeconds);
    }

    private void OnShutdown(Entity<NpcBuilderComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ActionEntity);
    }

    private void OnBuild(Entity<NpcBuilderComponent> ent, ref NpcBuildActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        ent.Comp.Built.RemoveAll(e => Deleted(e)); // forget destroyed structures so mob can rebuild up to the limit

        if (ent.Comp.Built.Count < ent.Comp.Limit)
            ent.Comp.Built.Add(Spawn(ent.Comp.Build, Transform(ent).Coordinates));

        _npc.SetBlackboard(ent, BuildCastTimeKey, (float) ent.Comp.BuildTime.TotalSeconds);
    }
}
