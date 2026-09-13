using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotSatedOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entities.TryGetComponent<RotSatedComponent>(owner, out var sated))
            return HTNOperatorStatus.Failed;
        _entities.System<RotSatedSystem>().Think((owner, sated));
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entities.TryGetComponent<RotSatedComponent>(owner, out var sated))
            _entities.System<RotSatedSystem>().Stop((owner, sated));
    }
}
