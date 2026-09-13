using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Runs colony decisions only while native HTN considers this NPC active.</summary>
public sealed partial class RotHungryOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entities.TryGetComponent<RotHungryComponent>(owner, out var hungry))
            return HTNOperatorStatus.Failed;

        _entities.System<RotHungrySystem>().Think((owner, hungry));
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        _entities.System<RotHungrySystem>().Stop(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }
}
