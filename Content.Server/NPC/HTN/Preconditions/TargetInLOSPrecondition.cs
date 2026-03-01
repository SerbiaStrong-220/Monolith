using Content.Server.NPC.Systems;
using Content.Shared.Physics;

namespace Content.Server.NPC.HTN.Preconditions;

public sealed partial class TargetInLOSPrecondition : HTNPrecondition
{
    [Dependency] private readonly IEntityManager _entManager = default!;
    private NPCCombatSystem _npcCombat = default!; // Exodus

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
        _npcCombat = _entManager.System<NPCCombatSystem>(); // Exodus
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!blackboard.TryGetValue<EntityUid>(TargetKey, out var target, _entManager))
            return false;

        var range = blackboard.GetValueOrDefault<float>(RangeKey, _entManager);
        return _npcCombat.IsEnemyInLOS(owner, ObstructedMask, BulletMask, target, range); // Exodus
    }
}
