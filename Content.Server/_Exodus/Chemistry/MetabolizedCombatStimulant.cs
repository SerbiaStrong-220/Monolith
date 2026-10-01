using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Chemistry;

/// <summary>Accounts only for the portion removed by this metabolism tick, never the remaining solution.</summary>
public sealed partial class MetabolizedCombatStimulant : EntityEffect
{
    [DataField]
    public TimeSpan DurationPerUnit = TimeSpan.FromSeconds(4);

    [DataField]
    public TimeSpan MaximumDuration = TimeSpan.FromSeconds(30);

    [DataField]
    public float MovementMultiplier = 1.2f;

    [DataField]
    public float AttackRateMultiplier = 1.3f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        // Method is null only during metabolism: splashes and ingestion reactions must not double-count a dose.
        if (args is not EntityEffectReagentArgs { Reagent: { } reagent, OrganEntity: not null, Method: null } dose ||
            dose.Quantity <= FixedPoint2.Zero)
            return;
        args.EntityManager.System<ChemicalDependencySystem>().TrySatisfy(args.TargetEntity, reagent.ID, dose.Quantity);
        args.EntityManager.System<CombatStimulantSystem>().TryApply(args.TargetEntity, dose.Quantity,
            DurationPerUnit, MaximumDuration, MovementMultiplier, AttackRateMultiplier);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys)
    {
        return Loc.GetString("reagent-effect-guidebook-combat-stimulant",
            ("movement", MathF.Round((MovementMultiplier - 1f) * 100f)), ("attack", MathF.Round((AttackRateMultiplier - 1f) * 100f)),
            ("duration", DurationPerUnit.TotalSeconds), ("maximum", MaximumDuration.TotalSeconds));
    }
}
