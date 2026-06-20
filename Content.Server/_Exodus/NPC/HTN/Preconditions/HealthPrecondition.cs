using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Damage;
using Content.Shared.Mobs.Systems;

namespace Content.Server._Exodus.NPC.HTN.Preconditions;

public sealed partial class HealthPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    private MobThresholdSystem _thresholds = default!;

    /// <summary>
    /// Blackboard key of the entity to check. Null means the NPC itself.
    /// </summary>
    [DataField]
    public string? Key;

    [DataField]
    public float MinPercent = 0f;

    [DataField]
    public float MaxPercent = 1f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _thresholds = sysManager.GetEntitySystem<MobThresholdSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        EntityUid uid;
        if (Key == null)
            uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        else if (!blackboard.TryGetValue<EntityUid>(Key, out uid, _entManager))
            return false;

        if (!_entManager.TryGetComponent(uid, out DamageableComponent? damage))
            return false;

        if (!_thresholds.TryGetIncapPercentage(uid, damage.TotalDamage, out var incap))
            return false;

        var health = 1f - (float) incap;
        return health >= MinPercent && health <= MaxPercent;
    }
}
