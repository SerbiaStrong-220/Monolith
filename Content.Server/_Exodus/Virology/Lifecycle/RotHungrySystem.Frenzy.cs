using System.Numerics;
using Content.Shared.Damage;
using Content.Shared.Mobs;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Virology.Lifecycle;

public sealed partial class RotHungrySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    private void InitializeFrenzy()
    {
        SubscribeLocalEvent<RotHungryComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<RotHungryComponent, GetMeleeAttackRateEvent>(OnGetAttackRate);
        SubscribeLocalEvent<RotHungryComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<RotHungryComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<RotHungryComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnRefreshSpeed(Entity<RotHungryComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Frenzied)
            args.ModifySpeed(ent.Comp.FrenzySpeedMultiplier, ent.Comp.FrenzySpeedMultiplier);
    }

    private void OnGetAttackRate(Entity<RotHungryComponent> ent, ref GetMeleeAttackRateEvent args)
    {
        if (ent.Comp.Frenzied)
            args.Multipliers *= ent.Comp.FrenzyAttackRateMultiplier;
    }

    private void OnMobStateChanged(Entity<RotHungryComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.NewMobState == MobState.Dead)
        {
            CancelDetour(ent);
            ResetRetreat(ent);
        }
    }

    private void OnPlayerAttached(Entity<RotHungryComponent> ent, ref PlayerAttachedEvent args)
    {
        ResetRetreat(ent);
        Stop(ent);
    }

    private void OnShutdown(Entity<RotHungryComponent> ent, ref ComponentShutdown args)
    {
        CancelDetour(ent);
        SetFrenzy(ent, false);
    }

    private void SetFrenzy(Entity<RotHungryComponent> ent, bool frenzied)
    {
        if (ent.Comp.Frenzied == frenzied)
            return;

        ent.Comp.Frenzied = frenzied;
        if (!TerminatingOrDeleted(ent))
            _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void ResetRetreat(Entity<RotHungryComponent> ent)
    {
        ent.Comp.Retreating = false;
        ent.Comp.Shelter = null;
        ent.Comp.Pursuer = null;
        ent.Comp.PursuitPositions.Clear();
        SetFrenzy(ent, false);
    }

    private bool TryEndRetreat(Entity<RotHungryComponent> ent)
    {
        if (!TryComp<DamageableComponent>(ent, out var damage)
            || !_thresholds.TryGetThresholdForState(ent, MobState.Dead, out var threshold)
            || damage.TotalDamage > threshold * ent.Comp.ReturnDamageFraction)
            return false;

        ResetRetreat(ent);
        return true;
    }

    private void UpdateRetreat(Entity<RotHungryComponent> ent)
    {
        if (TryEndRetreat(ent) || ent.Comp.Frenzied)
            return;

        if (!ent.Comp.Retreating)
        {
            if (!TryComp<DamageableComponent>(ent, out var damage)
                || !_thresholds.TryGetThresholdForState(ent, MobState.Dead, out var threshold)
                || damage.TotalDamage <= threshold * ent.Comp.RetreatDamageFraction)
                return;

            ent.Comp.Retreating = true;
            ent.Comp.RetreatUntil = _timing.CurTime + ent.Comp.RetreatDuration;
            ent.Comp.NextShelterSearch = _timing.CurTime;
            Stop(ent);
        }

        ObservePursuit(ent);
        if (_timing.CurTime < ent.Comp.RetreatUntil || _timing.CurTime > ent.Comp.PursuitUntil
            || ent.Comp.Pursuer is not { } pursuer || InvalidPrey(pursuer)
            || !_interaction.InRangeUnobstructed(ent.Owner, pursuer, ent.Comp.SearchRange))
            return;

        ResetRetreat(ent);
        ent.Comp.Target = pursuer;
        SetFrenzy(ent, true);
        Stop(ent);
        ent.Comp.LastMoved = _timing.CurTime;
    }

    private void RememberPursuer(Entity<RotHungryComponent> ent, EntityUid pursuer)
    {
        ent.Comp.Pursuer = pursuer;
        ent.Comp.PursuitUntil = _timing.CurTime + ent.Comp.PursuitMemory;
    }

    private void ObservePursuit(Entity<RotHungryComponent> ent)
    {
        var origin = _transform.GetMapCoordinates(ent);
        var nearest = float.MaxValue;
        foreach (var prey in ent.Comp.Prey)
        {
            if (!_transformQuery.TryComp(prey, out var transform) || _containers.IsEntityInContainer(prey))
                continue;

            var coordinates = transform.Coordinates;
            var hadPrevious = ent.Comp.PursuitPositions.TryGetValue(prey, out var previous);
            ent.Comp.PursuitPositions[prey] = coordinates;
            if (!hadPrevious || previous.EntityId != coordinates.EntityId || !previous.IsValid(EntityManager))
                continue;

            // Keeping observations relative to the grid excludes the ship's own movement and rotation.
            var position = _transform.GetMapCoordinates(prey, transform);
            var oldPosition = _transform.ToMapCoordinates(previous);
            var towardHungry = origin.Position - oldPosition.Position;
            var distance = Vector2.DistanceSquared(origin.Position, position.Position);
            if (position.MapId != origin.MapId || distance >= nearest
                || distance > ent.Comp.SearchRange * ent.Comp.SearchRange
                || Vector2.Dot(position.Position - oldPosition.Position, towardHungry)
                    <= ent.Comp.PursuitDistance * towardHungry.Length()
                || !_interaction.InRangeUnobstructed(ent.Owner, prey, ent.Comp.SearchRange))
                continue;

            nearest = distance;
            RememberPursuer(ent, prey);
        }
    }
}
