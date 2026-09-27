using Content.Shared._Exodus.DoAfter;
using Content.Shared._Exodus.Inventory;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Electrocution;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.StatusEffect;
using Content.Shared.Temperature;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared._Exodus.Genetics;

public sealed class SharedGeneticEffectsSystem : EntitySystem
{
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public const string TelekinesisRangeProvider = "GeneticTelekinesis";

    private EntityQuery<MobStateComponent> _mobStates;

    public override void Initialize()
    {
        base.Initialize();
        _mobStates = GetEntityQuery<MobStateComponent>();
        SubscribeLocalEvent<GeneticEffectsComponent, RefreshMovementSpeedModifiersEvent>(OnMovement);
        SubscribeLocalEvent<GeneticEffectsComponent, ComponentStartup>(OnChanged);
        SubscribeLocalEvent<GeneticEffectsComponent, AfterAutoHandleStateEvent>(OnChanged);
        SubscribeLocalEvent<GeneticEffectsComponent, GetAdditionalInventorySlotsEvent>(OnAdditionalSlots);
        SubscribeLocalEvent<GeneticEffectsComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<GeneticEffectsComponent, BeforeStatusEffectAddedEvent>(OnStatus);
        SubscribeLocalEvent<GeneticEffectsComponent, DamageModifyEvent>(OnDamage);
        SubscribeLocalEvent<GeneticEffectsComponent, GetMeleeDamageEvent>(OnMelee);
        SubscribeLocalEvent<GeneticEffectsComponent, BeforeStaminaDamageEvent>(OnStamina);
        SubscribeLocalEvent<GeneticEffectsComponent, RespirationAttemptEvent>(OnRespiration);
        SubscribeLocalEvent<GeneticEffectsComponent, PressureImmunityEvent>(OnPressure);
        SubscribeLocalEvent<GeneticEffectsComponent, TemperatureDamageAttemptEvent>(OnTemperatureDamage);
        SubscribeLocalEvent<GeneticEffectsComponent, ModifyChangedTemperatureEvent>(OnTemperatureChange);
        SubscribeLocalEvent<GeneticEffectsComponent, ValidateDoAfterRangeEvent>(OnValidateDoAfterRange);
        SubscribeLocalEvent<GeneticEffectsComponent, ElectrocutionAttemptEvent>(OnElectrocution);
        SubscribeLocalEvent<GeneticEffectsComponent, BleedAmountChangeEvent>(OnBleeding);
        SubscribeLocalEvent<GeneticEffectsComponent, FlashDurationModifyEvent>(OnFlashDuration);
        SubscribeLocalEvent<GeneticEffectsComponent, ShotAttemptedEvent>(OnShotAttempted);
    }

    private void OnShotAttempted(Entity<GeneticEffectsComponent> ent, ref ShotAttemptedEvent args)
    {
        if (args.Cancelled || ent.Comp.Reverting || !ent.Comp.Modifiers.BlockRangedWeapons)
            return;
        args.Cancel();
        if (!_net.IsClient || !_timing.IsFirstTimePredicted || _timing.CurTime < ent.Comp.NextBlockedShotPopup)
            return;
        ent.Comp.NextBlockedShotPopup = _timing.CurTime + ent.Comp.BlockedShotPopupInterval;
        _popup.PopupClient(Loc.GetString("genetics-hulk-cannot-shoot"), ent.Owner, ent.Owner);
    }

    private void OnAdditionalSlots(Entity<GeneticEffectsComponent> ent, ref GetAdditionalInventorySlotsEvent args)
    {
        if (ent.Comp.Reverting || (ent.Comp.Modifiers.Abilities & GeneticAbility.Pouch) == 0)
            return;
        args.Templates ??= new();
        args.Templates.Add(ent.Comp.PocketTemplate);
    }

    private void OnElectrocution(Entity<GeneticEffectsComponent> ent, ref ElectrocutionAttemptEvent args)
    {
        if (!ent.Comp.Reverting)
            args.SiemensCoefficient *= ent.Comp.Modifiers.ConductivityMultiplier;
    }

    private void OnBleeding(Entity<GeneticEffectsComponent> ent, ref BleedAmountChangeEvent args)
    {
        if (!ent.Comp.Reverting && args.Amount > 0)
            args.Amount *= ent.Comp.Modifiers.BleedingMultiplier;
    }

    private void OnFlashDuration(Entity<GeneticEffectsComponent> ent, ref FlashDurationModifyEvent args)
    {
        if (!ent.Comp.Reverting)
            args.Duration *= ent.Comp.Modifiers.FlashDurationMultiplier;
    }

    private void OnValidateDoAfterRange(Entity<GeneticEffectsComponent> ent, ref ValidateDoAfterRangeEvent args)
    {
        if (args.Args.RangeProvider != TelekinesisRangeProvider)
            return;

        args.Handled = true;
        args.Cancelled |= ent.Comp.Reverting || (ent.Comp.Modifiers.Abilities & GeneticAbility.Telekinesis) == 0 ||
                          !_mobStates.TryComp(ent, out var mob) || mob.CurrentState != MobState.Alive ||
                          args.Args.Target is not { } target || _mobStates.HasComp(target) ||
                          !_interaction.IsAccessible(ent.Owner, target);
    }

    private void OnChanged<T>(Entity<GeneticEffectsComponent> ent, ref T args)
    {
        _movement.RefreshMovementSpeedModifiers(ent);
        _inventory.RefreshSlots(ent.Owner);
    }

    private void OnShutdown(Entity<GeneticEffectsComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Reverting = true;
        _movement.RefreshMovementSpeedModifiers(ent);
        _inventory.RefreshSlots(ent.Owner);
        var ev = new GeneticEffectsShutdownEvent();
        RaiseLocalEvent(ent, ref ev);
    }

    private void OnMovement(Entity<GeneticEffectsComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!ent.Comp.Reverting)
            args.ModifySpeed(ent.Comp.Modifiers.MovementMultiplier, ent.Comp.Modifiers.MovementMultiplier);
    }

    private void OnStatus(Entity<GeneticEffectsComponent> ent, ref BeforeStatusEffectAddedEvent args)
    {
        if (!ent.Comp.Reverting && ent.Comp.Modifiers.BlockedStatuses.Contains(args.Key))
            args.Cancelled = true;
    }

    private void OnDamage(Entity<GeneticEffectsComponent> ent, ref DamageModifyEvent args)
    {
        var modifiers = ent.Comp.Modifiers;
        if (ent.Comp.Reverting || modifiers.DamageMultiplier == 1f &&
            modifiers.DamageModifiers.FlatReduction.Count == 0 && modifiers.DamageModifiers.Coefficients.Count == 0)
            return;

        // Healing is not amplified by a vulnerability.
        var damage = DamageSpecifier.ApplyModifierSet(args.Damage, modifiers.DamageModifiers);
        if (modifiers.DamageMultiplier != 1f)
        {
            foreach (var type in args.Damage.DamageDict.Keys)
            {
                if (damage.DamageDict.TryGetValue(type, out var amount) && amount > 0)
                    damage.DamageDict[type] = amount * modifiers.DamageMultiplier;
            }
        }
        args.Damage = damage;
    }

    private void OnMelee(Entity<GeneticEffectsComponent> ent, ref GetMeleeDamageEvent args)
    {
        if (!ent.Comp.Reverting && args.Weapon == ent.Owner)
            args.Damage *= ent.Comp.Modifiers.MeleeMultiplier;
    }

    private void OnStamina(Entity<GeneticEffectsComponent> ent, ref BeforeStaminaDamageEvent args)
    {
        if (!ent.Comp.Reverting && args.Value > 0)
            args.Value *= ent.Comp.Modifiers.StaminaMultiplier;
    }

    private void OnRespiration(Entity<GeneticEffectsComponent> ent, ref RespirationAttemptEvent args)
    {
        args.Cancelled |= !ent.Comp.Reverting && ent.Comp.Modifiers.NoBreathing;
    }

    private void OnPressure(Entity<GeneticEffectsComponent> ent, ref PressureImmunityEvent args)
    {
        args.Immune |= !ent.Comp.Reverting && (args.HighPressure
            ? ent.Comp.Modifiers.HighPressureImmunity
            : ent.Comp.Modifiers.LowPressureImmunity);
    }

    private void OnTemperatureDamage(Entity<GeneticEffectsComponent> ent, ref TemperatureDamageAttemptEvent args)
    {
        args.Cancelled |= !ent.Comp.Reverting && (args.Hot
            ? ent.Comp.Modifiers.HeatImmunity
            : ent.Comp.Modifiers.ColdImmunity);
    }

    private void OnTemperatureChange(Entity<GeneticEffectsComponent> ent, ref ModifyChangedTemperatureEvent args)
    {
        if (!ent.Comp.Reverting && (args.TemperatureDelta < 0 && ent.Comp.Modifiers.ColdImmunity ||
                                   args.TemperatureDelta > 0 && ent.Comp.Modifiers.HeatImmunity))
            args.TemperatureDelta = 0;
    }
}
