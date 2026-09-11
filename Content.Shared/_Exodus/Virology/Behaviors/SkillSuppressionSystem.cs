using Content.Shared._Goobstation.DoAfter;

namespace Content.Shared._Exodus.Virology.Behaviors;

public sealed class SkillSuppressionSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SkillSuppressionComponent, GetDoAfterDelayMultiplierEvent>(OnDelay);
    }

    private void OnDelay(Entity<SkillSuppressionComponent> ent, ref GetDoAfterDelayMultiplierEvent args)
    {
        args.Multiplier *= Math.Max(1f, ent.Comp.DelayMultiplier);
    }
}
