using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;

namespace Content.Server._Exodus.NPC.HTN.Preconditions;

public sealed partial class MobStatePrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    /// <summary>
    /// Blackboard key of the entity to check. Null means the NPC itself.
    /// </summary>
    [DataField]
    public string? Key;

    /// <summary>
    /// The mob state to look for.
    /// </summary>
    [DataField(required: true)]
    public MobState State;

    /// <summary>
    /// If true, the precondition is met when the entity is NOT in "State".
    /// </summary>
    [DataField]
    public bool Invert;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        EntityUid uid;
        if (Key == null)
            uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        else if (!blackboard.TryGetValue<EntityUid>(Key, out uid, _entManager))
            return false;

        if (!_entManager.TryGetComponent(uid, out MobStateComponent? state))
            return false;

        return (state.CurrentState == State) ^ Invert;
    }
}
