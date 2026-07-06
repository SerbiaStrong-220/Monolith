using Content.Server._Exodus.NPC.Abilities;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._Exodus.NPC.HTN.Preconditions;

public sealed partial class CanBuildPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<NpcBuilderComponent>(owner, out var comp))
            return false;

        var alive = 0;
        foreach (var e in comp.Built)
        {
            if (!_entManager.Deleted(e))
                alive++;
        }

        return alive < comp.Limit;
    }
}
