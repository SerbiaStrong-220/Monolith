using Robust.Shared.Prototypes;

namespace Content.Shared.Exodus.GameTicking;

[ImplicitDataDefinitionForInheritors]
public abstract partial class GameRuleRequirement
{
    public abstract bool Check(IEntityManager entityManager, IPrototypeManager prototypeManager);
}
