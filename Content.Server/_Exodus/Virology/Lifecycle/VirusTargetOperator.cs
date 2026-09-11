using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared._Exodus.Virology.Lifecycle;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Selects a susceptible host, then delegates movement and combat to native HTN operators.</summary>
public sealed partial class VirusTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entities = default!;

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entities.TryGetComponent<VirusOffspringComponent>(owner, out var offspring)
            || !_entities.System<VirusLifecycleSystem>().TryFindTarget((owner, offspring), out var target))
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));

        return Task.FromResult<(bool, Dictionary<string, object>?)>((true, new Dictionary<string, object>
        {
            { "Target", target },
            { "TargetCoordinates", new EntityCoordinates(target, Vector2.Zero) },
        }));
    }
}
