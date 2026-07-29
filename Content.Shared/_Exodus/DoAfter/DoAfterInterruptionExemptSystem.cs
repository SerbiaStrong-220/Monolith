using Content.Shared.DoAfter;
using Content.Shared.Movement.Systems;

namespace Content.Shared._Exodus.DoAfter;

public sealed class DoAfterInterruptionExemptSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DoAfterInterruptionExemptComponent, GetDoAfterInterruptionBreakEvent>(OnGetDoAfterInterruptionBreak);
        SubscribeLocalEvent<DoAfterInterruptionExemptComponent, DoAfterMovementSlowdownChangedEvent>(OnDoAfterMovementSlowdownChanged);
        SubscribeLocalEvent<DoAfterInterruptionExemptComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeedModifiers);
    }

    private void OnGetDoAfterInterruptionBreak(Entity<DoAfterInterruptionExemptComponent> ent,
        ref GetDoAfterInterruptionBreakEvent args)
    {
        if (ent.Comp.Exemptions.HasFlag(DoAfterInterruptionExemptions.Movement) && args.BreakOnMove)
        {
            args.BreakOnMove = false;
            args.ApplyMovementSlowdown = true;
        }

        if (ent.Comp.Exemptions.HasFlag(DoAfterInterruptionExemptions.HandChange))
            args.BreakOnHandChange = false;

        if (ent.Comp.Exemptions.HasFlag(DoAfterInterruptionExemptions.DropItem))
            args.BreakOnDropItem = false;
    }

    private void OnDoAfterMovementSlowdownChanged(Entity<DoAfterInterruptionExemptComponent> ent,
        ref DoAfterMovementSlowdownChangedEvent args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(ent);
    }

    private void OnRefreshMovementSpeedModifiers(Entity<DoAfterInterruptionExemptComponent> ent,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryComp<DoAfterComponent>(ent, out var doAfter))
            return;

        foreach (var active in doAfter.DoAfters.Values)
        {
            if (!active.Args.ApplyMovementSlowdown || active.Cancelled || active.Completed)
                continue;

            args.ModifySpeed(ent.Comp.WalkSpeedModifier, ent.Comp.SprintSpeedModifier);
            return;
        }
    }
}

[ByRefEvent]
public record struct GetDoAfterInterruptionBreakEvent(
    bool BreakOnMove,
    bool BreakOnHandChange,
    bool BreakOnDropItem,
    bool ApplyMovementSlowdown = false);

[ByRefEvent]
public record struct DoAfterMovementSlowdownChangedEvent;