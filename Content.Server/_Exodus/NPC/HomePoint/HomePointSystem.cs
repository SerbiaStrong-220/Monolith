using Content.Server.NPC.Systems;

namespace Content.Server._Exodus.NPC.HomePoint;

public sealed partial class HomePointSystem : EntitySystem
{
    [Dependency] private NPCSystem _npc = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HomePointComponent, MapInitEvent>(OnMapInit);
    }
    // Record NPC's spawn point
    private void OnMapInit(Entity<HomePointComponent> ent, ref MapInitEvent args)
    {
        _npc.SetBlackboard(ent, ent.Comp.Key, Transform(ent).Coordinates);
    }
}
