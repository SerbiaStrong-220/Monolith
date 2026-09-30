using Content.Server.IdentityManagement;
using Content.Shared._Exodus.Genetics;
using Content.Shared._Exodus.Stealth;
using Content.Shared._Exodus.Stealth.Components;
using Content.Shared._Exodus.Stealth.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Cloning;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _appearance = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private IdentitySystem _identity = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private GeneticsSystem _genetics = default!;

    private const string CloakSource = "Genetics";

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GeneticEffectsComponent, GenomeChangedEvent>(OnGenesChanged);
        SubscribeLocalEvent<GeneticAbilityStateComponent, GeneticEffectsShutdownEvent>(OnEffectsShutdown);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticMimicEvent>(OnMimic);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticRestoreAppearanceEvent>(OnRestore);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticCloakEvent>(OnCloak);
        SubscribeLocalEvent<GeneticAbilityStateComponent, ComponentShutdown>(OnStateShutdown);
        SubscribeLocalEvent<GeneticAbilityStateComponent, CloningSpeciesEvent>(OnCloningSpecies);
        SubscribeLocalEvent<GeneticAbilityStateComponent, CloningEvent>(OnCloning, before: new[] { typeof(GeneticsSystem) });
        SubscribeLocalEvent<GeneticAbilityStateComponent, StealthRevealEvent>(OnReveal);
        SubscribeLocalEvent<GeneticAbilityStateComponent, AttackedEvent>(OnAttacked);
        SubscribeLocalEvent<GeneticAbilityStateComponent, ProjectileHitTargetEvent>(OnProjectile);
        SubscribeLocalEvent<GeneticAbilityStateComponent, GunShotUserEvent>(OnGun);
        SubscribeLocalEvent<GeneticAbilityStateComponent, MeleeHitEvent>(OnMelee);
        SubscribeLocalEvent<GeneticAbilityStateComponent, MobStateChangedEvent>(OnMobState);
        InitializeViewing();
        InitializePrying();
        InitializeTelekinesis();
        InitializeAdaptations();
        InitializeDeflection();
        InitializeTransformation();
        InitializeCooldowns();
        InitializeCocoon();
    }

    private bool HasAbility(EntityUid uid, GeneticAbility ability)
    {
        return !TerminatingOrDeleted(uid) && TryComp<GeneticEffectsComponent>(uid, out var effects) &&
               !effects.Reverting && (effects.Modifiers.Abilities & ability) != 0;
    }

    private bool CanUse(EntityUid uid, GeneticAbility ability)
    {
        return HasAbility(uid, ability) && _blocker.CanInteract(uid, null) &&
               TryComp<MobStateComponent>(uid, out var state) && state.CurrentState == MobState.Alive;
    }

    private void OnGenesChanged(Entity<GeneticEffectsComponent> ent, ref GenomeChangedEvent args)
    {
        _inventory.RefreshSlots(ent.Owner);
        var state = EnsureComp<GeneticAbilityStateComponent>(ent);
        if (!HasAbility(ent, GeneticAbility.Cloak))
            StopCloak((ent.Owner, state), false);
        if (!HasAbility(ent, GeneticAbility.Mimic))
            RestoreAppearance((ent.Owner, state));
        if (!HasAbility(ent, GeneticAbility.RemoteViewing))
            StopViewing((ent.Owner, state), true);
        ReconcileTransformation(ent, state);
        ReconcileAdaptations(ent, state);
        RefreshGeneticCooldowns((ent.Owner, state));
    }

    private void OnEffectsShutdown(Entity<GeneticAbilityStateComponent> ent, ref GeneticEffectsShutdownEvent args)
    {
        Cleanup(ent);
        RemCompDeferred<GeneticAbilityStateComponent>(ent);
    }

    private void OnStateShutdown(Entity<GeneticAbilityStateComponent> ent, ref ComponentShutdown args)
    {
        Cleanup(ent);
    }

    private void OnCloningSpecies(Entity<GeneticAbilityStateComponent> ent, ref CloningSpeciesEvent args)
    {
        if ((ent.Comp.GeneticOriginalAppearance ?? ent.Comp.OriginalAppearance ?? ent.Comp.FormAppearance) is { } original)
            args.Species = original.Species;
    }

    private void OnCloning(Entity<GeneticAbilityStateComponent> ent, ref CloningEvent args)
    {
        if (TerminatingOrDeleted(args.Target) || !TryComp<HumanoidAppearanceComponent>(ent, out var current))
            return;
        var state = EnsureComp<GeneticAbilityStateComponent>(args.Target);
        StopTransformation((args.Target, state));
        var visible = ent.Comp.FormAppearance ?? current;
        var original = ent.Comp.GeneticOriginalAppearance ?? ent.Comp.OriginalAppearance ?? visible;

        // First-time genome initialization reconciles inactive genes and clears mimicry state.
        // Do it on the biological appearance before installing the source's restoration snapshots.
        _appearance.ApplyAppearance(args.Target, original);
        if (!_genetics.TryGetGenome(args.Target, out _))
            return;

        state.OriginalAppearance = ent.Comp.OriginalAppearance == null
            ? null : _serialization.CreateCopy(original, notNullableOverride: true);
        state.OriginalName = ent.Comp.OriginalName;
        // Genetics applies the copied genome afterwards, rebuilding adaptations on the original anatomy.
        _appearance.ApplyAppearance(args.Target, state.OriginalAppearance == null ? original : visible);
        state.TransformationColor = ent.Comp.TransformationColor;
        state.TransformationAvailable = ent.Comp.TransformationAvailable;
    }

    private void Cleanup(Entity<GeneticAbilityStateComponent> ent)
    {
        StopViewing(ent, true);
        CleanupAdaptations(ent);
        StopTransformation(ent);
        if (!TerminatingOrDeleted(ent))
        {
            StopCloak(ent, false);
            RestoreAppearance(ent);
        }
    }

    private void Reveal(EntityUid uid)
    {
        var ev = new StealthRevealEvent(uid);
        RaiseLocalEvent(uid, ev);
    }

    private void OnMimic(Entity<GeneticEffectsComponent> ent, ref GeneticMimicEvent args)
    {
        if (args.Handled || ent.Comp.InAlternateForm || !CanUse(ent, GeneticAbility.Mimic) || args.Target == ent.Owner ||
            !_interaction.InRangeUnobstructed(ent.Owner, args.Target) ||
            !TryComp<HumanoidAppearanceComponent>(ent, out var appearance) ||
            !TryComp<HumanoidAppearanceComponent>(args.Target, out var target))
            return;
        var state = EnsureComp<GeneticAbilityStateComponent>(ent);
        state.OriginalAppearance ??= _serialization.CreateCopy(appearance, notNullableOverride: true);
        state.OriginalName ??= MetaData(ent).EntityName;
        Reveal(ent);
        _appearance.CloneAppearance(args.Target, ent, target, appearance);
        _metadata.SetEntityName(ent, Identity.Name(args.Target, EntityManager));
        _identity.QueueIdentityUpdate(ent);
        args.Handled = true;
    }

    private void OnRestore(Entity<GeneticEffectsComponent> ent, ref GeneticRestoreAppearanceEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.Mimic) ||
            !TryComp<GeneticAbilityStateComponent>(ent, out var state))
            return;
        RestoreAppearance((ent.Owner, state));
        args.Handled = true;
    }

    private void RestoreAppearance(Entity<GeneticAbilityStateComponent> ent)
    {
        if (ent.Comp.OriginalAppearance is not { } original || TerminatingOrDeleted(ent))
            return;
        ExitAlternateForm(ent);
        _appearance.ApplyAppearance(ent.Owner, original);
        if (ent.Comp.OriginalName is { } name)
            _metadata.SetEntityName(ent, name);
        _identity.QueueIdentityUpdate(ent);
        ent.Comp.OriginalAppearance = null;
        ent.Comp.OriginalName = null;
    }

    private void OnCloak(Entity<GeneticEffectsComponent> ent, ref GeneticCloakEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.Cloak))
            return;
        var state = EnsureComp<GeneticAbilityStateComponent>(ent);
        if (state.Cloaked)
        {
            StopCloak((ent.Owner, state), false);
            args.Handled = true;
            return;
        }
        if (_stealth.IsSuppressed(ent))
        {
            _popup.PopupEntity(Loc.GetString("stealth-disruptor-suppressed"), ent, ent);
            return;
        }
        if (state.CloakAvailable > _timing.CurTime)
        {
            _popup.PopupEntity(Loc.GetString("genetics-cloak-cooldown"), ent, ent);
            return;
        }
        state.Cloaked = _stealth.RequestStealth(ent, CloakSource, new StealthData());
        args.Handled = state.Cloaked;
    }

    private void StopCloak(Entity<GeneticAbilityStateComponent> ent, bool broken)
    {
        if (!ent.Comp.Cloaked)
            return;
        _stealth.RemoveRequest(CloakSource, ent);
        ent.Comp.Cloaked = false;
        if (broken)
            ent.Comp.CloakAvailable = _timing.CurTime + ent.Comp.CloakCooldown;
        RefreshGeneticCooldowns(ent);
    }

    private void OnReveal(Entity<GeneticAbilityStateComponent> ent, ref StealthRevealEvent args) => StopCloak(ent, true);
    private void OnAttacked(Entity<GeneticAbilityStateComponent> ent, ref AttackedEvent args) => StopCloak(ent, true);
    private void OnProjectile(Entity<GeneticAbilityStateComponent> ent, ref ProjectileHitTargetEvent args) => StopCloak(ent, true);
    private void OnGun(Entity<GeneticAbilityStateComponent> ent, ref GunShotUserEvent args) => StopCloak(ent, true);

    private void OnMelee(Entity<GeneticAbilityStateComponent> ent, ref MeleeHitEvent args)
    {
        if (args.HitEntities.Count != 0)
            StopCloak(ent, true);
    }

    private void OnMobState(Entity<GeneticAbilityStateComponent> ent, ref MobStateChangedEvent args)
    {
        HandleTransformationMobState(ent, args.NewMobState);
        if (args.NewMobState == MobState.Alive)
            return;
        StopCloak(ent, true);
        StopViewing(ent, true);
        StopHearing(ent.Owner);
    }
}
