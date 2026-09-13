using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared._Exodus.Virology.Lifecycle;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotLarvaFeedOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entities.TryGetComponent<RotLarvaComponent>(owner, out var larva))
            return HTNOperatorStatus.Failed;
        if (larva.Satiety >= larva.MaxSatiety)
            return HTNOperatorStatus.Continuing;
        if (!blackboard.TryGetValue<EntityUid>("Target", out var target, _entities)
            || !_entities.System<RotNestSystem>().TryFeed((owner, larva), target))
            return HTNOperatorStatus.Failed;
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (_entities.TryGetComponent<RotLarvaComponent>(owner, out var larva))
            larva.NextBite = null;
    }
}
