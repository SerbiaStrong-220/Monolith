using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared._Exodus.Virology.Lifecycle;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotLarvaPupateOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var full = _entities.TryGetComponent<RotLarvaComponent>(owner, out var larva) && larva.Satiety >= larva.MaxSatiety;
        return Task.FromResult<(bool, Dictionary<string, object>?)>((full, null));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime) => HTNOperatorStatus.Continuing;
}
