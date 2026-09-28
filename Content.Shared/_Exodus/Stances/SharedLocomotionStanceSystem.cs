using Content.Shared._Exodus.Hands;
using Content.Shared.Interaction.Events;
using Content.Shared.Item;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;

namespace Content.Shared._Exodus.Stances;

/// <summary>Predicted action and movement restrictions, shared by all anatomical stances.</summary>
public abstract class SharedLocomotionStanceSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LocomotionStanceComponent, HandAddAttemptEvent>(OnAddHand);
        SubscribeLocalEvent<LocomotionStanceComponent, InteractionAttemptEvent>(OnInteract);
        SubscribeLocalEvent<LocomotionStanceComponent, UseAttemptEvent>(OnUse);
        SubscribeLocalEvent<LocomotionStanceComponent, PickupAttemptEvent>(OnPickup);
        SubscribeLocalEvent<LocomotionStanceComponent, AttackAttemptEvent>(OnAttack);
        SubscribeLocalEvent<LocomotionStanceComponent, UpdateCanMoveEvent>(OnCanMove);
        SubscribeLocalEvent<LocomotionStanceComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    public static bool CanUseHands(LocomotionStanceComponent stance) =>
        stance.Stance == LocomotionStance.Upright && stance.TransitionEnd == TimeSpan.Zero;

    private void OnAddHand(Entity<LocomotionStanceComponent> ent, ref HandAddAttemptEvent args)
    {
        args.Cancelled |= !CanUseHands(ent.Comp);
    }

    private void OnInteract(Entity<LocomotionStanceComponent> ent, ref InteractionAttemptEvent args)
    {
        args.Cancelled |= !CanUseHands(ent.Comp);
    }

    private void OnUse(Entity<LocomotionStanceComponent> ent, ref UseAttemptEvent args)
    {
        if (!CanUseHands(ent.Comp))
            args.Cancel();
    }

    private void OnPickup(Entity<LocomotionStanceComponent> ent, ref PickupAttemptEvent args)
    {
        if (!CanUseHands(ent.Comp))
            args.Cancel();
    }

    private void OnAttack(Entity<LocomotionStanceComponent> ent, ref AttackAttemptEvent args)
    {
        if (ent.Comp.Stance == LocomotionStance.Curled || ent.Comp.TransitionEnd != TimeSpan.Zero)
            args.Cancel();
    }

    private void OnCanMove(Entity<LocomotionStanceComponent> ent, ref UpdateCanMoveEvent args)
    {
        if (ent.Comp.Stance == LocomotionStance.Curled || ent.Comp.TransitionEnd != TimeSpan.Zero)
            args.Cancel();
    }

    private void OnRefreshSpeed(Entity<LocomotionStanceComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Stance == LocomotionStance.Quadruped)
            args.ModifySpeed(ent.Comp.QuadrupedSpeedMultiplier);
    }
}
