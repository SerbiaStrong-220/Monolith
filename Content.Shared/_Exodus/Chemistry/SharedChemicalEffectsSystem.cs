using Content.Shared.Alert.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Shared._Exodus.Chemistry;

/// <summary>Predicted movement and attack-rate modifiers owned by the affected body, including held weapons.</summary>
public abstract partial class SharedChemicalEffectsSystem : EntitySystem
{
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<CombatStimulantComponent> _stimulants;
    private EntityQuery<ChemicalDependencyComponent> _dependencies;

    public override void Initialize()
    {
        base.Initialize();
        _stimulants = GetEntityQuery<CombatStimulantComponent>();
        _dependencies = GetEntityQuery<ChemicalDependencyComponent>();
        SubscribeLocalEvent<CombatStimulantComponent, RefreshMovementSpeedModifiersEvent>(OnStimulantMovement);
        SubscribeLocalEvent<CombatStimulantComponent, ComponentStartup>(OnStimulantChanged);
        SubscribeLocalEvent<CombatStimulantComponent, AfterAutoHandleStateEvent>(OnStimulantChanged);
        SubscribeLocalEvent<CombatStimulantComponent, ComponentShutdown>(OnStimulantShutdown);
        SubscribeLocalEvent<ChemicalDependencyComponent, RefreshMovementSpeedModifiersEvent>(OnDependencyMovement);
        SubscribeLocalEvent<ChemicalDependencyComponent, ComponentStartup>(OnDependencyStartup);
        SubscribeLocalEvent<ChemicalDependencyComponent, AfterAutoHandleStateEvent>(OnDependencyChanged);
        SubscribeLocalEvent<ChemicalDependencyComponent, ComponentShutdown>(OnDependencyShutdown);
        SubscribeLocalEvent<ChemicalDependencyComponent, GetGenericAlertCounterAmountEvent>(OnAlertCounter);
        SubscribeLocalEvent<MeleeWeaponComponent, GetMeleeAttackRateEvent>(OnAttackRate);
    }

    private void OnStimulantMovement(Entity<CombatStimulantComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.ExpiresAt > _timing.CurTime)
            args.ModifySpeed(ent.Comp.MovementMultiplier, ent.Comp.MovementMultiplier);
    }

    private void OnDependencyMovement(Entity<ChemicalDependencyComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(ent.Comp.MovementMultiplier, ent.Comp.MovementMultiplier);
    }

    private void OnAttackRate(Entity<MeleeWeaponComponent> ent, ref GetMeleeAttackRateEvent args)
    {
        if (_stimulants.TryComp(args.User, out var stimulant) && stimulant.ExpiresAt > _timing.CurTime)
            args.Multipliers *= stimulant.AttackRateMultiplier;
        if (_dependencies.TryComp(args.User, out var dependency))
            args.Multipliers *= dependency.AttackRateMultiplier;
    }

    private void OnStimulantChanged<T>(Entity<CombatStimulantComponent> ent, ref T args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnDependencyChanged<T>(Entity<ChemicalDependencyComponent> ent, ref T args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    protected virtual void OnDependencyStartup(Entity<ChemicalDependencyComponent> ent, ref ComponentStartup args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnStimulantShutdown(Entity<CombatStimulantComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.MovementMultiplier = 1f;
        ent.Comp.AttackRateMultiplier = 1f;
        if (!TerminatingOrDeleted(ent))
            _movement.RefreshMovementSpeedModifiers(ent);
    }

    protected virtual void OnDependencyShutdown(Entity<ChemicalDependencyComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.MovementMultiplier = 1f;
        ent.Comp.AttackRateMultiplier = 1f;
        if (!TerminatingOrDeleted(ent))
            _movement.RefreshMovementSpeedModifiers(ent);
    }

    private void OnAlertCounter(Entity<ChemicalDependencyComponent> ent, ref GetGenericAlertCounterAmountEvent args)
    {
        if (args.Alert.ID == ent.Comp.Alert.Id)
            args.Amount = ent.Comp.RemainingMinutes;
    }
}
