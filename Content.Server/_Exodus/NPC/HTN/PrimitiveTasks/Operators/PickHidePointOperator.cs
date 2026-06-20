using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Robust.Shared.Map;

namespace Content.Server._Exodus.NPC.HTN.PrimitiveTasks.Operators;

public sealed partial class PickHidePointOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private PathfindingSystem _pathfinding = default!;
    private SharedInteractionSystem _interaction = default!;
    private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Blackboard key of the entity to hide from.
    /// </summary>
    [DataField]
    public string TargetKey = "Target";

    /// <summary>
    /// How far to search for a retreat point. Needs further testing (or ideally we should somehow mutate this range based on enviroment)
    /// </summary>
    [DataField]
    public float Range = 10f;

    /// <summary>
    /// How many random paths to generate before pick furthest one.
    /// </summary>
    [DataField]
    public int MaxAttempts = 5;

    /// <summary>
    /// Where the chosen coordinates are stored.
    /// </summary>
    [DataField]
    public string TargetCoordinates = "TargetCoordinates";

    /// <summary>
    /// Where the pathfinding result is stored (for MoveToOperator).
    /// </summary>
    [DataField]
    public string PathfindKey = NPCBlackboard.PathfindKey;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
        _interaction = sysManager.GetEntitySystem<SharedInteractionSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var enemy, _entManager))
            return (false, null);

        var enemyPos = _transform.GetMapCoordinates(enemy);
        var flags = _pathfinding.GetFlags(blackboard);

        PathResultEvent? bestFallback = null;
        var bestCoords = EntityCoordinates.Invalid;
        var bestDistance = -1f;

        for (var i = 0; i < MaxAttempts; i++)
        {
            var path = await _pathfinding.GetRandomPath(owner, Range, cancelToken, flags: flags);

            if (path.Result != PathResult.Path || path.Path.Count == 0)
                continue;

            var point = path.Path[^1].Coordinates;

            // Is a structure breaking LoS. (can enemy see that spot?)
            if (!_interaction.InRangeUnobstructed(enemy, point, 0f, CollisionGroup.Impassable, e => e == owner))
                return Result(point, path);

            // Finding point to retreat to, if enemy LoS everywhere just keep the one furthest from the enemy.
            var dist = (enemyPos.Position - _transform.ToMapCoordinates(point).Position).Length();
            if (dist > bestDistance)
            {
                bestDistance = dist;
                bestFallback = path;
                bestCoords = point;
            }
        }

        if (bestFallback != null)
            return Result(bestCoords, bestFallback);

        return (false, null);
    }

    private (bool, Dictionary<string, object>?) Result(EntityCoordinates coords, PathResultEvent path)
    {
        return (true, new Dictionary<string, object>
        {
            { TargetCoordinates, coords },
            { PathfindKey, path },
        });
    }
}
