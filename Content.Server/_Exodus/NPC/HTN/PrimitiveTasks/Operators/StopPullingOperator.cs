using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Movement.Pulling.Components;
using Content.Shared.Movement.Pulling.Systems;

namespace Content.Server._Exodus.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class StopPullingOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private PullingSystem _pulling = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pulling = sysManager.GetEntitySystem<PullingSystem>();
    }
    // Stops the current pull via toggle.
    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (_entManager.TryGetComponent<PullerComponent>(owner, out var puller))
            _pulling.TogglePull(owner, puller);

        return HTNOperatorStatus.Finished;
    }
}
