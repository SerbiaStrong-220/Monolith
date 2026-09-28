using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotRetaliationOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var active = _entities.TryGetComponent<RotRetaliationComponent>(owner, out var retaliation)
            && retaliation.Target != null;
        return Task.FromResult<(bool, Dictionary<string, object>?)>((active, null));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return _entities.TryGetComponent<RotRetaliationComponent>(owner, out var retaliation)
            && _entities.System<RotRetaliationSystem>().Think((owner, retaliation))
            ? HTNOperatorStatus.Continuing : HTNOperatorStatus.Finished;
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        _entities.System<RotRetaliationSystem>().Begin(blackboard.GetValue<EntityUid>(NPCBlackboard.Owner));
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entities.TryGetComponent<RotRetaliationComponent>(owner, out var retaliation))
            _entities.System<RotRetaliationSystem>().StopMovement((owner, retaliation));
    }
}
