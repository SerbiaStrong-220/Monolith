using Content.Server.Temperature.Components;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Damage;
using Content.Shared.EntityEffects;
using Content.Shared.Mobs;
using Content.Shared.Popups;
using Content.Shared.Temperature.Components;
using Content.Shared.Temperature.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticsSystem
{
    [Dependency] private IComponentFactory _geneticComponents = default!;
    [Dependency] private ISerializationManager _geneticSerialization = default!;
    [Dependency] private SharedTemperatureSystem _temperatureSpeed = default!;
    [Dependency] private SharedPopupSystem _geneticPopup = default!;

    private void MutationConflict(EntityUid target, EntityUid actor)
    {
        if (!TerminatingOrDeleted(actor))
            _geneticPopup.PopupEntity(Loc.GetString("genetics-conflicting-mutations"), target, actor);
    }

    private bool CanActivate(List<ushort> blocks, ProtoId<GeneticMutationPrototype> candidate, int skip)
    {
        var round = GetRound();
        var mutation = _prototypes.Index(candidate);
        for (var i = 0; i < blocks.Count && i < round.Mutations.Count; i++)
        {
            if (i == skip || round.Mutations[i] is not { } id || !IsBlockActive(blocks[i], round.Thresholds[i]))
                continue;

            var other = _prototypes.Index(id);
            if (mutation.Conflicts.Contains(id) || other.Conflicts.Contains(candidate))
                return false;
            // Two genes must never compete for ownership of the same component.
            foreach (var name in mutation.Components.Keys)
            {
                if (other.Components.ContainsKey(name))
                    return false;
            }
        }
        return true;
    }

    private bool HasCompatibleMutations(List<ushort> blocks)
    {
        var round = GetRound();
        for (var i = 0; i < blocks.Count; i++)
        {
            if (round.Mutations[i] is { } id && IsBlockActive(blocks[i], round.Thresholds[i]) && !CanActivate(blocks, id, i))
                return false;
        }
        return true;
    }

    private void ReconcileSpeciesEffects(Entity<GenomeComponent> ent)
    {
        var removed = new List<string>();
        foreach (var (name, grant) in ent.Comp.ComponentGrants)
        {
            if (ent.Comp.Active.Contains(grant.Mutation))
                continue;
            RestoreGeneticComponent(ent, name, grant);
            removed.Add(name);
        }
        foreach (var name in removed)
            ent.Comp.ComponentGrants.Remove(name);

        var thermal = new GeneticThermalModifiers();
        ent.Comp.TemperatureEffects.Clear();
        foreach (var id in ent.Comp.Active)
        {
            var mutation = _prototypes.Index(id);
            thermal.ColdThresholdOffset += mutation.Thermal.ColdThresholdOffset;
            thermal.HeatThresholdOffset += mutation.Thermal.HeatThresholdOffset;
            thermal.ColdDamageMultiplier *= mutation.Thermal.ColdDamageMultiplier;
            thermal.HeatDamageMultiplier *= mutation.Thermal.HeatDamageMultiplier;
            ent.Comp.TemperatureEffects.AddRange(mutation.TemperatureEffects);
            foreach (var (name, entry) in mutation.Components)
            {
                if (ent.Comp.ComponentGrants.ContainsKey(name))
                    continue;

                var registration = _geneticComponents.GetRegistration(name);
                IComponent? original = null;
                if (EntityManager.TryGetComponent(ent.Owner, registration.Type, out var current))
                    original = _geneticSerialization.CreateCopy(current, notNullableOverride: true);
                var replacement = ent.Comp.DormantComponents.Remove(name, out var dormant)
                    ? dormant
                    : _geneticSerialization.CreateCopy(entry.Component, notNullableOverride: true);
                AddComp(ent.Owner, replacement, true);
                ent.Comp.ComponentGrants.Add(name, new GeneticComponentGrant
                {
                    Mutation = id,
                    Applied = replacement,
                    Original = original,
                    Preserve = mutation.PreserveComponents.Contains(name),
                });
            }
        }
        ApplyThermalModifiers(ent, thermal);
    }

    private void RestoreGeneticComponent(Entity<GenomeComponent> ent, string name, GeneticComponentGrant grant)
    {
        var registration = _geneticComponents.GetRegistration(name);
        if (!EntityManager.TryGetComponent(ent.Owner, registration.Type, out var current) || !ReferenceEquals(current, grant.Applied))
            return;
        if (grant.Preserve)
            ent.Comp.DormantComponents[name] = _geneticSerialization.CreateCopy(current, notNullableOverride: true);
        if (grant.Original == null)
            RemComp(ent.Owner, current);
        else
            AddComp(ent.Owner, grant.Original, true);
    }

    private void ApplyThermalModifiers(Entity<GenomeComponent> ent, GeneticThermalModifiers modifiers)
    {
        if (!TryComp<TemperatureComponent>(ent, out var temperature))
            return;
        if (ent.Comp.ThermalState is not { } saved || !ReferenceEquals(saved.Component, temperature))
        {
            saved = new GeneticThermalState
            {
                Component = temperature,
                ColdThreshold = temperature.ColdDamageThreshold,
                HeatThreshold = temperature.HeatDamageThreshold,
                ColdDamage = new DamageSpecifier(temperature.ColdDamage),
                HeatDamage = new DamageSpecifier(temperature.HeatDamage),
            };
            ent.Comp.ThermalState = saved;
        }
        temperature.ColdDamageThreshold = saved.ColdThreshold + modifiers.ColdThresholdOffset;
        temperature.HeatDamageThreshold = saved.HeatThreshold + modifiers.HeatThresholdOffset;
        temperature.ColdDamage = saved.ColdDamage * modifiers.ColdDamageMultiplier;
        temperature.HeatDamage = saved.HeatDamage * modifiers.HeatDamageMultiplier;
        if (TryComp<TemperatureSpeedComponent>(ent, out var speed))
            _temperatureSpeed.RefreshTemperatureSpeed((ent.Owner, speed), temperature.CurrentTemperature);
    }

    private void ClearSpeciesEffects(Entity<GenomeComponent> ent)
    {
        foreach (var (name, grant) in ent.Comp.ComponentGrants)
            RestoreGeneticComponent(ent, name, grant);
        ent.Comp.ComponentGrants.Clear();
        ent.Comp.TemperatureEffects.Clear();
        ApplyThermalModifiers(ent, new GeneticThermalModifiers());
        ent.Comp.ThermalState = null;
    }

    private void OnSpeciesEffectsShutdown(Entity<GenomeComponent> ent, ref GeneticEffectsShutdownEvent args)
    {
        if (TerminatingOrDeleted(ent))
            return;
        ClearSpeciesEffects(ent);
        ent.Comp.EffectsInitialized = false;
    }

    private void UpdateTemperatureEffects(Entity<GenomeComponent> ent, MobState state)
    {
        if (state != MobState.Alive || ent.Comp.TemperatureEffects.Count == 0 ||
            !TryComp<TemperatureComponent>(ent, out var temperature))
            return;
        var args = new EntityEffectBaseArgs(ent.Owner, EntityManager);
        foreach (var conditional in ent.Comp.TemperatureEffects)
        {
            if (temperature.CurrentTemperature < conditional.MinimumTemperature || temperature.CurrentTemperature > conditional.MaximumTemperature)
                continue;
            foreach (var effect in conditional.Effects)
                effect.Effect(args);
        }
    }
}
