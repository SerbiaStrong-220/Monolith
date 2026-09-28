using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._Exodus.Virology.Intelligent;

public sealed partial class RotRallyOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var valid = _entities.System<RotIntelligentSystem>().TryGetRally(owner, out var point);
        return Task.FromResult<(bool, Dictionary<string, object>?)>((valid,
            valid ? new() { ["TargetCoordinates"] = point } : null));
    }
}
