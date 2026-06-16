using Content.Server.Interaction;
using Content.Server.NPC.Systems;
using Content.Shared.Damage.Components;
using Content.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Server.NPC.HTN.Preconditions;

public sealed partial class TargetInLOSPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    private InteractionSystem _interaction = default!;
    private NPCCombatSystem _npcCombat = default!; // Exodus
    // Mono
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<RequireProjectileTargetComponent> _requireTargetQuery;

    [DataField("targetKey")]
    public string TargetKey = "Target";

    [DataField("rangeKey")]
    public string RangeKey = "RangeKey";

    // Mono
    [DataField]
    public CollisionGroup ObstructedMask = CollisionGroup.Opaque;

    // Mono
    [DataField]
    public CollisionGroup BulletMask = CollisionGroup.Impassable | CollisionGroup.BulletImpassable;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _interaction = sysManager.GetEntitySystem<InteractionSystem>();
        _npcCombat = _entManager.System<NPCCombatSystem>(); // Exodus
        // Mono
        _physicsQuery = _entManager.GetEntityQuery<PhysicsComponent>();
        _requireTargetQuery = _entManager.GetEntityQuery<RequireProjectileTargetComponent>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return false;

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);
        return _interaction.InRangeUnobstructed(owner, target, range, ObstructedMask, predicate: (EntityUid entity) =>
        {
            return _physicsQuery.TryGetComponent(entity, out var physics) && (physics.CollisionLayer & (int)BulletMask) == 0 // ignore if it can't collide with bullets
                || _requireTargetQuery.HasComponent(entity); // or if it requires targeting
        }) && _npcCombat.IsNoEnemyInLOS(owner, BulletMask, target, range);
    }
}
