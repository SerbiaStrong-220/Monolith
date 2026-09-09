using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Movement.Pulling.Systems;

namespace Content.Server._Exodus.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class PullOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private PullingSystem _pulling = default!;

    [DataField]
    public string TargetKey = "Target";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pulling = sysManager.GetEntitySystem<PullingSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager) || _entManager.Deleted(target))
            return HTNOperatorStatus.Failed;

        return _pulling.TryStartPull(owner, target)
            ? HTNOperatorStatus.Finished
            : HTNOperatorStatus.Failed;
    }
}
