using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Shared._Exodus.NPC.Command;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.NPC.Command;

// TEST
public sealed class SquadStatusLightSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPointLightSystem _light = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = _timing.CurTime;

        var query = EntityQueryEnumerator<SquadStatusLightComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (now < comp.NextUpdate)
                continue;

            comp.NextUpdate = now + comp.UpdateInterval;

            if (!_light.TryGetLight(uid, out var light))
                continue;

            _light.SetColor(uid, GetColor(uid, comp), light);
        }
    }

    private Color GetColor(EntityUid uid, SquadStatusLightComponent comp)
    {
        if (TryComp<HTNComponent>(uid, out var htn)
            && htn.Blackboard.TryGetValue<Enum>(NPCBlackboard.CurrentOrders, out var raw, EntityManager)
            && raw is NpcOrder order)
        {
            return order switch
            {
                NpcOrder.Attack => comp.Attacking,
                NpcOrder.Retreat => comp.Retreating,
                NpcOrder.Hold => comp.Holding,
                NpcOrder.Follow => comp.InSquad,
                _ => comp.Other,
            };
        }

        return HasComp<NpcCommanderComponent>(uid) ? comp.InSquad : comp.NoCommander;
    }
}
