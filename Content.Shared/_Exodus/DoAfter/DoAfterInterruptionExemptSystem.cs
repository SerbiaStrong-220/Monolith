using Content.Shared.DoAfter;
using Content.Shared.Movement.Systems;
using Content.Shared._White.Standing;
using Robust.Shared.Player;

namespace Content.Shared._Exodus.DoAfter;

public sealed class DoAfterInterruptionExemptSystem : EntitySystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DoAfterInterruptionExemptComponent, GetDoAfterInterruptionBreakEvent>(OnGetDoAfterInterruptionBreak);
        SubscribeLocalEvent<DoAfterInterruptionExemptComponent, DoAfterMovementSlowdownChangedEvent>(OnDoAfterMovementSlowdownChanged);
        SubscribeLocalEvent<DoAfterInterruptionExemptComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeedModifiers);
        SubscribeNetworkEvent<ChangeLayingDownEvent>(OnChangeLayingDown);
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

    private void OnChangeLayingDown(ChangeLayingDownEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { } uid ||
            !TryComp<DoAfterInterruptionExemptComponent>(uid, out var exemption))
        {
            return;
        }

        CancelMobileDoAfters((uid, exemption));
    }

    private void CancelMobileDoAfters(Entity<DoAfterInterruptionExemptComponent> ent)
    {
        if (!TryComp<DoAfterComponent>(ent, out var doAfter))
            return;

        var toCancel = new List<ushort>();
        foreach (var (id, active) in doAfter.DoAfters)
        {
            if (active.Args.ApplyMovementSlowdown && !active.Cancelled && !active.Completed)
                toCancel.Add(id);
        }

        foreach (var id in toCancel)
        {
            _doAfter.Cancel(ent.Owner, id, doAfter);
        }
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