using System.Threading;
using System.Threading.Tasks;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Actions;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.NPC.HTN.PrimitiveTasks.Operators;

public enum UseActionTargetMode : byte
{
    /// <summary>No target. For instant actions (AOE-on-self, summon).</summary>
    None,

    /// <summary>The NPC itself (self-heal, self-buff).</summary>
    Self,

    /// <summary>An entity read from blackboard.</summary>
    Entity,

    /// <summary>Coordinates read from blackboard.</summary>
    Coordinates,

    /// <summary>Tile the NPC is standing on.</summary>
    SelfCoordinates,

    /// <summary>A point X tiles away in a random direction.</summary>
    RandomNearby,

    /// <summary>A point X tiles away in the direction pointing away from the entity.</summary>
    AwayFromTarget,
}

/// For this to work action must already be present in the NPC's prototype. 
/// Triggers an action that has been granted to the NPC.
/// This operator finds it by prototype id and performs it.
public sealed partial class UseActionOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;
    private SharedActionsSystem _actions = default!;
    private SharedTransformSystem _transform = default!;

    /// <summary>Kinda squared float for "we're standing on top of the target" before bail in a random direction.</summary>
    private const float SamePositionEpsilon = 0.01f;

    /// <summary>Prototype of the granted action to use.</summary>
    [DataField(required: true)]
    public EntProtoId ActionId;

    /// <summary>Where the action is aimed.</summary>
    [DataField]
    public UseActionTargetMode TargetMode = UseActionTargetMode.None;

    /// <summary>Blackboard key read for the target entity.</summary>
    [DataField]
    public string TargetKey = "Target";

    /// <summary>Distance in tiles for RandomNearby and AwayFromTarget.</summary>
    [DataField]
    public float Distance = 4f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _actions = sysManager.GetEntitySystem<SharedActionsSystem>();
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var valid = TryGetAction(owner, out _, out _)
                    && TryResolveTarget(blackboard, owner, out _, out _);

        return Task.FromResult<(bool, Dictionary<string, object>?)>((valid, null));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!TryGetAction(owner, out var actionEnt, out var action))
            return HTNOperatorStatus.Failed;

        if (!_actions.ValidAction(action))
            return HTNOperatorStatus.Failed;

        if (!TryResolveTarget(blackboard, owner, out var targetEntity, out var targetCoords))
            return HTNOperatorStatus.Failed;

        // Bail if target doesn't match what the action event needs otherwise
        // PerformAction would run with a stale Target left over from a previous invocation (events are reused).
        switch (action.BaseEvent)
        {
            case EntityTargetActionEvent entEv:
                if (targetEntity == null)
                    return HTNOperatorStatus.Failed;
                entEv.Target = targetEntity.Value;
                break;
            case WorldTargetActionEvent worldEv:
                if (targetCoords == null)
                    return HTNOperatorStatus.Failed;
                worldEv.Target = targetCoords.Value;
                break;
            case EntityWorldTargetActionEvent ewEv:
                ewEv.Entity = targetEntity;
                ewEv.Coords = targetCoords;
                break;
        }

        _actions.PerformAction(owner, null, actionEnt, action, action.BaseEvent, _timing.CurTime, false);
        return HTNOperatorStatus.Finished;
    }

    private bool TryGetAction(EntityUid owner, out EntityUid actionEnt, out BaseActionComponent action)
    {
        foreach (var (id, comp) in _actions.GetActions(owner))
        {
            if (!_entManager.TryGetComponent<MetaDataComponent>(id, out var meta) || meta.EntityPrototype?.ID != ActionId.Id)
                continue;

            actionEnt = id;
            action = comp;
            return true;
        }

        actionEnt = default;
        action = default!;
        return false;
    }

    private bool TryResolveTarget(NPCBlackboard blackboard, EntityUid owner, out EntityUid? entity, out EntityCoordinates? coords)
    {
        entity = null;
        coords = null;

        switch (TargetMode)
        {
            case UseActionTargetMode.None:
                return true;
            case UseActionTargetMode.Self:
                entity = owner;
                return true;
            case UseActionTargetMode.Entity:
                if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var ent, _entManager))
                    return false;
                entity = ent;
                return true;
            case UseActionTargetMode.Coordinates:
                if (!blackboard.TryGetValue<EntityCoordinates>(TargetKey, out var c, _entManager))
                    return false;
                coords = c;
                return true;
            case UseActionTargetMode.SelfCoordinates:
                if (!_entManager.TryGetComponent<TransformComponent>(owner, out var selfXform))
                    return false;
                coords = selfXform.Coordinates;
                return true;
            case UseActionTargetMode.RandomNearby:
                if (!_entManager.TryGetComponent<TransformComponent>(owner, out var randomXform))
                    return false;
                // Offset in the parent (grid) frame; direction is random so the frame does not matter.
                coords = randomXform.Coordinates.Offset(_random.NextAngle().ToVec() * Distance);
                return true;
            case UseActionTargetMode.AwayFromTarget:
                if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var from, _entManager))
                    return false;

                if (!_entManager.TryGetComponent<TransformComponent>(owner, out var ownerXform))
                    return false;

                var ownerMap = _transform.GetMapCoordinates(owner, ownerXform);
                var fromMap = _transform.GetMapCoordinates(from);

                if (ownerMap.MapId != fromMap.MapId)
                    return false;

                var worldDir = ownerMap.Position - fromMap.Position;
                var dist = worldDir.Length();
                // Convert the world-space away direction into the grid frame so the offset is correct
                // on rotated grids. If we are standing on top of the target, just bail in a random direction.
                var localDir = dist < SamePositionEpsilon
                    ? _random.NextAngle().ToVec()
                    : (-_transform.GetWorldRotation(ownerXform.ParentUid)).RotateVec(worldDir / dist);

                coords = ownerXform.Coordinates.Offset(localDir * Distance);
                return true;
            default:
                return false;
        }
    }
}
