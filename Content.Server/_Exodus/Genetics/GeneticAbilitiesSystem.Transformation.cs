using Content.Server.Body.Systems;
using Content.Shared._Exodus.Genetics;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Damage;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    [Dependency] private IPrototypeManager _transformationPrototypes = default!;
    [Dependency] private IRobustRandom _transformationRandom = default!;
    [Dependency] private MovementSpeedModifierSystem _transformationMovement = default!;

    private void InitializeTransformation()
    {
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticTransformEvent>(OnTransform);
        SubscribeLocalEvent<GeneticAbilityStateComponent, BodyPartAddedEvent>(OnTransformationPartAdded, before: new[] { typeof(BodySystem) });
        SubscribeLocalEvent<GeneticAbilityStateComponent, BodyPartRemovedEvent>(OnTransformationPartRemoved, before: new[] { typeof(BodySystem) });
        InitializeTransformationPhysiology();
    }

    private void ReconcileTransformation(Entity<GeneticEffectsComponent> ent, GeneticAbilityStateComponent state)
    {
        var desired = ent.Comp.Modifiers.Transformation;
        if (state.Transformation == desired)
            return;

        StopTransformation((ent.Owner, state));
        if (desired == null || !HasComp<BodyComponent>(ent) || HasComp<GeneticIncompatibleComponent>(ent) ||
            !TryComp<HumanoidAppearanceComponent>(ent, out var appearance) ||
            !_transformationPrototypes.TryIndex(desired, out var profile))
            return;

        state.Transformation = desired;
        var underlying = state.OriginalAppearance ?? appearance;
        state.NativeTransformation = underlying.Species == profile.Species;
        if (state.NativeTransformation)
            return;

        state.GeneticOriginalAppearance = _serialization.CreateCopy(underlying, notNullableOverride: true);
        var disguise = state.OriginalAppearance == null ? null : _serialization.CreateCopy(appearance, notNullableOverride: true);
        _appearance.ApplyAppearance(ent.Owner, underlying);
        _appearance.SetSpecies(ent, profile.Species, humanoid: appearance);
        state.TransformationColor ??= profile.Colors.Count > 0 ? _transformationRandom.Pick(profile.Colors) : appearance.SkinColor;
        _appearance.SetSkinColor(ent, state.TransformationColor.Value, humanoid: appearance);
        foreach (var (part, bodyPart) in _transformationBody.GetBodyChildren(ent))
        {
            if (HasComp<CyberneticsComponent>(part) || bodyPart.ToHumanoidLayers() is not { } layer ||
                !appearance.CustomBaseLayers.TryGetValue(layer, out var info))
                continue;
            if (info.Id != null && _transformationPrototypes.TryIndex<HumanoidSpeciesSpriteLayer>(info.Id, out var sprite) && sprite.MatchSkin)
                _appearance.SetBaseLayerColor(ent, layer, state.TransformationColor.Value.WithAlpha(sprite.LayerAlpha), humanoid: appearance);
        }
        if (disguise != null)
        {
            state.OriginalAppearance = _serialization.CreateCopy(appearance, notNullableOverride: true);
            _appearance.ApplyAppearance(ent.Owner, disguise);
        }
        ApplyTransformationPhysiology((ent.Owner, state), profile);
        _identity.QueueIdentityUpdate(ent);
    }

    private void StopTransformation(Entity<GeneticAbilityStateComponent> ent)
    {
        ExitAlternateForm(ent);
        if (ent.Comp.Transformation == null)
            return;

        RestoreTransformationPhysiology(ent);
        if (ent.Comp.GeneticOriginalAppearance is { } original && !TerminatingOrDeleted(ent))
        {
            if (ent.Comp.OriginalAppearance != null)
                ent.Comp.OriginalAppearance = _serialization.CreateCopy(original, notNullableOverride: true);
            else
                _appearance.ApplyAppearance(ent.Owner, original);
            _identity.QueueIdentityUpdate(ent);
        }
        ent.Comp.GeneticOriginalAppearance = null;
        ent.Comp.Transformation = null;
        ent.Comp.NativeTransformation = false;
    }

    private void OnTransform(Entity<GeneticEffectsComponent> ent, ref GeneticTransformEvent args)
    {
        if (args.Handled || !TryComp<GeneticAbilityStateComponent>(ent, out var state))
            return;
        if (state.FormAppearance != null)
        {
            ExitAlternateForm((ent.Owner, state));
            args.Handled = true;
            return;
        }
        if (ent.Comp.Reverting || state.Transformation == null ||
            ent.Comp.Modifiers.Transformation != state.Transformation ||
            !TryComp<MobStateComponent>(ent, out var mob) || mob.CurrentState != MobState.Alive ||
            !_blocker.CanInteract(ent, null) || _containers.IsEntityOrParentInContainer(ent) ||
            !TryComp<HumanoidAppearanceComponent>(ent, out var appearance) ||
            !_transformationPrototypes.TryIndex(state.Transformation, out var profile) ||
            !Ready(ent, state.TransformationAvailable))
            return;

        // Equipment is dropped normally. The mutation cannot bypass locked clothing or stuck items.
        if (!DropTransformationEquipment(ent))
        {
            _popup.PopupEntity(Loc.GetString("genetics-transform-equipment-blocked"), ent, ent);
            return;
        }
        Reveal(ent);
        var formColor = profile.FormMatchesSkinColor
            ? (state.OriginalAppearance ?? appearance).SkinColor
            : profile.FormColor;
        state.FormAppearance = _serialization.CreateCopy(appearance, notNullableOverride: true);
        appearance.CustomBaseLayers.Clear();
        appearance.MarkingSet = new MarkingSet();
        _appearance.SetSpecies(ent, profile.FormSpecies, humanoid: appearance);
        _appearance.SetSkinColor(ent, formColor, verify: false, humanoid: appearance);
        ent.Comp.InAlternateForm = true;
        state.TransformationAvailable = _timing.CurTime + profile.Cooldown;
        RefreshGeneticCooldowns((ent.Owner, state));
        Dirty(ent);
        _transformationMovement.RefreshMovementSpeedModifiers(ent);
        _identity.QueueIdentityUpdate(ent);
        args.Handled = true;
    }

    private bool DropTransformationEquipment(EntityUid uid)
    {
        if (_inventory.TryGetContainerSlotEnumerator(uid, out var check))
        {
            while (check.MoveNext(out var slot))
            {
                if (slot.ContainedEntity != null && !_inventory.CanUnequip(uid, slot.ID, out _))
                    return false;
            }
        }
        foreach (var held in _hands.EnumerateHeld(uid))
        {
            if (!_hands.TryDrop(uid, held))
                return false;
        }
        if (_inventory.TryGetContainerSlotEnumerator(uid, out var slots))
        {
            while (slots.MoveNext(out var slot))
            {
                if (slot.ContainedEntity != null && !_inventory.TryUnequip(uid, slot.ID, silent: true))
                    return false;
            }
        }
        return true;
    }

    private void ExitAlternateForm(Entity<GeneticAbilityStateComponent> ent)
    {
        if (ent.Comp.FormAppearance is not { } appearance)
            return;
        ent.Comp.FormAppearance = null;
        if (TerminatingOrDeleted(ent))
            return;
        _appearance.ApplyAppearance(ent.Owner, appearance);
        if (TryComp<GeneticEffectsComponent>(ent, out var effects))
        {
            effects.InAlternateForm = false;
            Dirty(ent.Owner, effects);
        }
        _transformationMovement.RefreshMovementSpeedModifiers(ent);
        _identity.QueueIdentityUpdate(ent);
        RefreshGeneticCooldowns(ent);
    }

    private void HandleTransformationMobState(Entity<GeneticAbilityStateComponent> ent, MobState state)
    {
        if (ent.Comp.FormAppearance == null || state is not (MobState.Critical or MobState.Dead))
            return;
        var heal = state == MobState.Critical &&
                   _transformationPrototypes.TryIndex(ent.Comp.Transformation, out var profile) && profile.HealOnCriticalReturn;
        ExitAlternateForm(ent);
        if (!heal)
            return;
        var uid = ent.Owner;
        // Finish the critical-state event before healing: a nested state change can leave later listeners in crit.
        Timer.Spawn(TimeSpan.Zero, () =>
        {
            if (TerminatingOrDeleted(uid) || !TryComp<MobStateComponent>(uid, out var mob) ||
                !mob.Initialized || mob.CurrentState == MobState.Dead || !TryComp<DamageableComponent>(uid, out var damage))
                return;
            _transformationDamage.SetAllDamage(uid, damage, 0);
        });
    }

    private void OnTransformationPartAdded(Entity<GeneticAbilityStateComponent> ent, ref BodyPartAddedEvent args)
    {
        // Restore the humanoid layout before surgery applies the new limb's visuals.
        ExitAlternateForm(ent);
    }

    private void OnTransformationPartRemoved(Entity<GeneticAbilityStateComponent> ent, ref BodyPartRemovedEvent args)
    {
        ExitAlternateForm(ent);
    }
}
