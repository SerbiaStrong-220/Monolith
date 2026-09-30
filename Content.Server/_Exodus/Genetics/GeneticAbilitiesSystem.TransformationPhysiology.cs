using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Exodus.Genetics;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    [Dependency] private BodySystem _transformationBody = default!;
    [Dependency] private MetabolizerSystem _transformationMetabolism = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private DamageableSystem _transformationDamage = default!;
    [Dependency] private IComponentFactory _transformationFactory = default!;
    [Dependency] private LungSystem _transformationLungs = default!;

    private void InitializeTransformationPhysiology()
    {
        SubscribeLocalEvent<MetabolizerComponent, OrganAddedToBodyEvent>(OnTransformationOrganAdded);
        SubscribeLocalEvent<MetabolizerComponent, OrganRemovedFromBodyEvent>(OnTransformationOrganRemoved);
    }

    private void ApplyTransformationPhysiology(Entity<GeneticAbilityStateComponent> ent, GeneticTransformationPrototype profile)
    {
        if (TryComp<BloodstreamComponent>(ent, out var blood))
        {
            ent.Comp.OriginalBloodReagent = blood.BloodReagent;
            _bloodstream.ChangeBloodReagent(ent, profile.BloodReagent, blood);
        }
        if (TryComp<DamageableComponent>(ent, out var damage))
        {
            ent.Comp.OriginalDamageModifier = damage.DamageModifierSetId;
            ent.Comp.ChangedDamageModifier = true;
            _transformationDamage.SetDamageModifierSetId(ent, profile.DamageModifierSet, damage);
        }
        foreach (var (organ, _) in _transformationBody.GetBodyOrgans(ent))
        {
            if (TryComp<MetabolizerComponent>(organ, out var metabolizer))
                AdaptMetabolizer(ent, (organ, metabolizer), profile);
        }
        foreach (var (name, entry) in profile.Components)
        {
            var registration = _transformationFactory.GetRegistration(name);
            IComponent? original = null;
            if (EntityManager.TryGetComponent(ent.Owner, registration.Type, out var component))
                original = _serialization.CreateCopy(component, notNullableOverride: true);
            var replacement = _serialization.CreateCopy(entry.Component, notNullableOverride: true);
            AddComp(ent.Owner, replacement, true);
            ent.Comp.OriginalPhysiology.Add(name, (original, replacement));
        }
    }

    private void RestoreTransformationPhysiology(Entity<GeneticAbilityStateComponent> ent)
    {
        foreach (var (organ, original) in ent.Comp.OriginalMetabolizers)
        {
            if (!TerminatingOrDeleted(organ) && TryComp<MetabolizerComponent>(organ, out var metabolizer))
                _transformationMetabolism.SetMetabolizerTypes((organ, metabolizer), original);
        }
        ent.Comp.OriginalMetabolizers.Clear();
        foreach (var (organ, alert) in ent.Comp.OriginalBreathingAlerts)
        {
            if (!TerminatingOrDeleted(organ) && TryComp<LungComponent>(organ, out var lung))
                _transformationLungs.SetBreathingAlert((organ, lung), alert);
        }
        ent.Comp.OriginalBreathingAlerts.Clear();
        if (!TerminatingOrDeleted(ent))
        {
            if (ent.Comp.OriginalBloodReagent is { } blood)
                _bloodstream.ChangeBloodReagent(ent, blood);
            if (ent.Comp.ChangedDamageModifier)
                _transformationDamage.SetDamageModifierSetId(ent, ent.Comp.OriginalDamageModifier?.Id);
            foreach (var (name, saved) in ent.Comp.OriginalPhysiology)
            {
                var registration = _transformationFactory.GetRegistration(name);
                if (!EntityManager.TryGetComponent(ent.Owner, registration.Type, out var current) ||
                    !ReferenceEquals(current, saved.Applied))
                    continue;
                if (saved.Original == null)
                    RemComp(ent.Owner, current);
                else
                    AddComp(ent.Owner, saved.Original, true);
            }
        }
        ent.Comp.OriginalPhysiology.Clear();
        ent.Comp.OriginalBloodReagent = null;
        ent.Comp.OriginalDamageModifier = null;
        ent.Comp.ChangedDamageModifier = false;
    }

    private void AdaptMetabolizer(Entity<GeneticAbilityStateComponent> ent, Entity<MetabolizerComponent> organ,
        GeneticTransformationPrototype profile)
    {
        if (ent.Comp.NativeTransformation || HasComp<CyberneticsComponent>(organ) || ent.Comp.OriginalMetabolizers.ContainsKey(organ))
            return;
        ent.Comp.OriginalMetabolizers.Add(organ, organ.Comp.MetabolizerTypes == null ? null : new(organ.Comp.MetabolizerTypes));
        _transformationMetabolism.SetMetabolizerTypes(organ, new(profile.MetabolizerTypes));
        if (TryComp<LungComponent>(organ, out var lung))
        {
            ent.Comp.OriginalBreathingAlerts.Add(organ, lung.Alert);
            _transformationLungs.SetBreathingAlert((organ.Owner, lung), profile.BreathingAlert);
        }
    }

    private void OnTransformationOrganAdded(Entity<MetabolizerComponent> ent, ref OrganAddedToBodyEvent args)
    {
        if (TryComp<GeneticAbilityStateComponent>(args.Body, out var state) && state.Transformation != null &&
            _transformationPrototypes.TryIndex(state.Transformation, out var profile))
            AdaptMetabolizer((args.Body, state), ent, profile);
    }

    private void OnTransformationOrganRemoved(Entity<MetabolizerComponent> ent, ref OrganRemovedFromBodyEvent args)
    {
        if (!TryComp<GeneticAbilityStateComponent>(args.OldBody, out var state))
            return;
        if (state.OriginalMetabolizers.Remove(ent.Owner, out var original))
            _transformationMetabolism.SetMetabolizerTypes(ent, original);
        if (state.OriginalBreathingAlerts.Remove(ent.Owner, out var alert) && TryComp<LungComponent>(ent, out var lung))
            _transformationLungs.SetBreathingAlert((ent.Owner, lung), alert);
    }
}
