using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Actions;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.NPC.HTN.Preconditions;

public sealed partial class AbilityReadyPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    private SharedActionsSystem _actions = default!;

    /// <summary>
    /// This is validates action as good-to-go for HTN tree so it can choose it.
    /// Prototype of the action that must be ready.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId ActionId;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _actions = sysManager.GetEntitySystem<SharedActionsSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        foreach (var (id, action) in _actions.GetActions(owner))
        {
            if (!_entManager.TryGetComponent<MetaDataComponent>(id, out var meta) || meta.EntityPrototype?.ID != ActionId.Id)
                continue;

            return _actions.ValidAction(action, canReach: true);
        }

        return false;
    }
}
