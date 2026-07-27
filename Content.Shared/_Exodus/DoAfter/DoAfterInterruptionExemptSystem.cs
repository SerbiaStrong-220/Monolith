namespace Content.Shared._Exodus.DoAfter;

public sealed class DoAfterInterruptionExemptSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DoAfterInterruptionExemptComponent, GetDoAfterInterruptionBreakEvent>(OnGetDoAfterInterruptionBreak);
    }

    private void OnGetDoAfterInterruptionBreak(Entity<DoAfterInterruptionExemptComponent> ent,
        ref GetDoAfterInterruptionBreakEvent args)
    {
        if (ent.Comp.Exemptions.HasFlag(DoAfterInterruptionExemptions.Movement))
            args.BreakOnMove = false;

        if (ent.Comp.Exemptions.HasFlag(DoAfterInterruptionExemptions.HandChange))
            args.BreakOnHandChange = false;

        if (ent.Comp.Exemptions.HasFlag(DoAfterInterruptionExemptions.DropItem))
            args.BreakOnDropItem = false;
    }
}

[ByRefEvent]
public record struct GetDoAfterInterruptionBreakEvent(
    bool BreakOnMove,
    bool BreakOnHandChange,
    bool BreakOnDropItem);