// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared._Exodus.CCVar;
using Content.Server.Nutrition.Components;
using Content.Shared.Atmos;
using Content.Shared.Chemistry;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Nutrition.Components;
using Content.Shared._Exodus.Virology;
using Robust.Shared.Random;

namespace Content.Server._Exodus.Virology;

public sealed partial class VirologySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedInternalsSystem _internals = default!;

    private EntityQuery<InternalsComponent> _internalsQuery;
    private EntityQuery<GasTankComponent> _gasTankQuery;

    private const float MinAirbornePressure = Atmospherics.HazardLowPressure;

    private readonly List<(EntityUid Uid, VirusHolderComponent Holder)> _holderBuf = [];
    private readonly HashSet<Entity<VirusSusceptibleComponent>> _nearbyHosts = [];
    private readonly HashSet<Entity<FoodComponent>> _nearbyFood = [];
    private readonly HashSet<Entity<DrinkComponent>> _nearbyDrinks = [];

    private void InitializeSpread()
    {
        _internalsQuery = GetEntityQuery<InternalsComponent>();
        _gasTankQuery = GetEntityQuery<GasTankComponent>();
        SubscribeLocalEvent<VirusSusceptibleComponent, ContactInteractionEvent>(OnContact);
        SubscribeLocalEvent<VirusSusceptibleComponent, ReactionEntityEvent>(OnReaction);
        SubscribeLocalEvent<VirusProtectionComponent, InventoryRelayedEvent<VirusAddAttempt>>(OnProtection);
    }

    private void OnReaction(Entity<VirusSusceptibleComponent> ent, ref ReactionEntityEvent args)
    {
        if (args.Method != ReactionMethod.Touch)
            return;

        // Equipment can block splashes, but does not grant immunity.
        if (IsVectorBlocked(ent.Owner, VirusTransmissionVector.Splash))
            return;

        InfectFromReagent(ent.Owner, args.ReagentQuantity.Reagent);
    }

    private void TickSpread()
    {
        _holderBuf.Clear();
        var query = EntityQueryEnumerator<VirusHolderComponent>();
        while (query.MoveNext(out var uid, out var holder))
        {
            if (holder.Viruses.Count == 0)
                continue;

            if (_mobState.IsDead(uid))
                continue;

            _holderBuf.Add((uid, holder));
        }

        foreach (var (uid, holder) in _holderBuf)
            TryTransmitProximity(uid, holder);
    }

    private void TryTransmitProximity(EntityUid source, VirusHolderComponent holder)
    {
        if (IsVectorBlocked(source, VirusTransmissionVector.Proximity))
            return;

        if (_atmos.GetContainingMixture(source) is not { } air || air.Pressure < MinAirbornePressure)
            return;

        var coords = _transform.GetMapCoordinates(source);

        foreach (var strain in EnumerateStrains(holder))
        {
            if (!TryGetActiveTransmission(strain.Comp, out var transmission) || transmission.ProximityChance <= 0f)
                continue;

            var descriptor = ToDescriptor(strain);

            _nearbyHosts.Clear();
            _lookup.GetEntitiesInRange(coords, transmission.ProximityRange, _nearbyHosts);
            foreach (var (target, _) in _nearbyHosts)
            {
                if (target == source)
                    continue;

                if (!_random.Prob(transmission.ProximityChance))
                    continue;

                if (!_interaction.InRangeUnobstructed(source, target, transmission.ProximityRange))
                    continue;

                if (IsVectorBlocked(target, VirusTransmissionVector.Proximity, inhaling: true))
                    continue;

                AddVirus(target, descriptor);
            }

            // airborne strains settle on nearby food/drink
            _nearbyFood.Clear();
            _lookup.GetEntitiesInRange(coords, transmission.ProximityRange, _nearbyFood);
            foreach (var (food, _) in _nearbyFood)
            {
                if (_random.Prob(transmission.ProximityChance)
                    && _interaction.InRangeUnobstructed(source, food, transmission.ProximityRange))
                    Contaminate(food, descriptor);
            }

            _nearbyDrinks.Clear();
            _lookup.GetEntitiesInRange(coords, transmission.ProximityRange, _nearbyDrinks);
            foreach (var (drink, _) in _nearbyDrinks)
            {
                if (!HasComp<FoodComponent>(drink) && _random.Prob(transmission.ProximityChance)
                    && _interaction.InRangeUnobstructed(source, drink, transmission.ProximityRange))
                    Contaminate(drink, descriptor);
            }
        }
    }

    private void OnContact(Entity<VirusSusceptibleComponent> ent, ref ContactInteractionEvent args)
    {
        // mob <-> mob: each infected side spreads to the other (runs from both parties, once per direction)
        if (HasComp<VirusSusceptibleComponent>(args.Other))
        {
            if (TryComp<VirusHolderComponent>(ent, out var holder))
                TryTransmitContact((ent.Owner, holder), args.Other);

            return;
        }

        // infected host leaves its contact-vector strains on a touched item
        if (TryComp<VirusHolderComponent>(ent, out var selfHolder)
            && !IsVectorBlocked(ent.Owner, VirusTransmissionVector.Contact))
        {
            foreach (var strain in EnumerateStrains(selfHolder))
            {
                if (TryGetActiveTransmission(strain.Comp, out var transmission) && transmission.ContactChance > 0f)
                    Contaminate(args.Other, ToDescriptor(strain));
            }
        }

        // contaminated item passes its strains
        if (TryComp<VirusContaminantComponent>(args.Other, out var contaminant)
            && !IsVectorBlocked(ent.Owner, VirusTransmissionVector.Contact))
        {
            foreach (var strain in contaminant.Viruses)
            {
                var descriptor = strain.Descriptor;
                if (strain.ExpiresAt > _timing.CurTime
                    && !IsBloodOnly(descriptor)
                    && ResolveTransmission(descriptor) is { ContactChance: > 0f } transmission
                    && _random.Prob(transmission.ContactChance))
                    AddVirus(ent.Owner, descriptor);
            }
        }
    }

    private void TryTransmitContact(Entity<VirusHolderComponent> source, EntityUid target)
    {
        if (IsVectorBlocked(source.Owner, VirusTransmissionVector.Contact)
            || IsVectorBlocked(target, VirusTransmissionVector.Contact))
            return;

        foreach (var strain in EnumerateStrains(source.Comp))
        {
            if (!TryGetActiveTransmission(strain.Comp, out var transmission) || transmission.ContactChance <= 0f)
                continue;

            if (_random.Prob(transmission.ContactChance))
                AddVirus(target, ToDescriptor(strain));
        }
    }

    private bool TryGetActiveTransmission(VirusComponent comp, out VirusTransmission transmission)
    {
        transmission = default!;
        if (comp.SuppressedUntil != null || IsBloodOnly(comp) || comp.Transmission is not { } profile)
            return false;

        transmission = profile;
        return true;
    }

    private bool IsVectorBlocked(EntityUid entity, VirusTransmissionVector vector, bool inhaling = false)
    {
        return _random.Prob(GetProtectionChance(entity, vector, inhaling));
    }

    /// <summary>Combines equipped protection and, for incoming airborne exposure, the actual breathing supply.</summary>
    public float GetProtectionChance(EntityUid entity, VirusTransmissionVector vector, bool inhaling = true)
    {
        var attempt = new VirusAddAttempt(entity, vector);
        RaiseLocalEvent(entity, ref attempt);

        if (inhaling && vector == VirusTransmissionVector.Proximity
            && _internalsQuery.TryGetComponent(entity, out var internals)
            && _internals.AreInternalsWorking(internals)
            && _gasTankQuery.TryGetComponent(internals.GasTankEntity, out var tank)
            && tank.User == entity
            && Math.Min(tank.OutputPressure, tank.Air.Pressure) >= MinAirbornePressure)
        {
            var bonus = Math.Clamp(_cfg.GetCVar(EXCVars.VirologyInternalsProtection), 0f, 1f);
            attempt.BlockChance += (1f - attempt.BlockChance) * bonus;
        }

        // Roll once after combining independent barriers so stacking cannot produce complete protection.
        return Math.Clamp(attempt.BlockChance, 0f, 0.99f);
    }

    private void OnProtection(Entity<VirusProtectionComponent> ent, ref InventoryRelayedEvent<VirusAddAttempt> args)
    {
        if ((ent.Comp.Vectors & args.Args.Vector) == 0)
            return;

        args.Args.BlockChance += (1f - args.Args.BlockChance) * Math.Clamp(ent.Comp.BlockChance, 0f, 1f);
    }
}
