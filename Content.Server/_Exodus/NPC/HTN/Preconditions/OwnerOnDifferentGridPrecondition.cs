using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared._Exodus.NPC.Pet;

namespace Content.Server._Exodus.NPC.HTN.Preconditions;

public sealed partial class OwnerOnDifferentGridPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        // NPCBlackboard.Owner is pet itself, player-master in PetComponent.
        var pet = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<PetComponent>(pet, out var comp) || comp.Master is not { } master)
            return false;

        if (_entManager.Deleted(master))
            return false;

        if (!_entManager.TryGetComponent<TransformComponent>(pet, out var petXform) ||
            !_entManager.TryGetComponent<TransformComponent>(master, out var masterXform))
            return false;

        return petXform.GridUid != masterXform.GridUid;
    }
}
