// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Lokilife
using Robust.Shared.Prototypes;

namespace Content.Shared.Exodus.GameTicking.Requirements;

[ImplicitDataDefinitionForInheritors]
public abstract partial class GameRuleRequirement
{
    public abstract bool Check(IEntityManager entityManager, IPrototypeManager prototypeManager);
}
