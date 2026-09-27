using Content.Shared.EntityEffects;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Genetics.Effects;

/// <summary>Disables acquired mutations on metabolism, without modifying virus samples or innate species traits.</summary>
public sealed partial class StabilizeGenome : EntityEffect
{
    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("genetics-genostabilin-effect");
    }

    public override void Effect(EntityEffectBaseArgs args)
    {
        args.EntityManager.System<GeneticsSystem>().TryStabilize(args.TargetEntity);
    }
}
