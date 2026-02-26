// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Content.Server.Exodus.GameTicking.Requirements;
using Robust.Shared.Prototypes;

namespace Content.Server.Exodus.GameTicking.Requirements;

// TODO: this system should probably be implemented via observer pattern but I'm too lazy currently
public sealed partial class GameRuleRequirementsSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototype = default!;

    public bool CheckRule(EntityUid uid)
    {
        if (!TryComp<GameRuleRequirementsComponent>(uid, out var requirements))
            return true;

        foreach (var requirement in requirements.Requirements)
            if (!requirement.Check(EntityManager, _prototype))
                return false;

        // all requirements passed
        return true;
    }
}
