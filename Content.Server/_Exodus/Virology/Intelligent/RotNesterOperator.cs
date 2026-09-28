using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotNesterOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entities.TryGetComponent<RotNesterComponent>(uid, out var nester))
            return HTNOperatorStatus.Failed;
        _entities.System<RotNesterSystem>().Think((uid, nester));
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entities.TryGetComponent<RotNesterComponent>(uid, out var nester))
            _entities.System<RotNesterSystem>().Stop((uid, nester));
    }
}
