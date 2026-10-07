// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server.Storage.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Containers;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketBasketSystem
{
    private bool AddEntity(EntityUid uid, BuildState state, int depth, EntityUid? appraisalGrid)
    {
        if (!AddEntityOwn(uid, state, depth, appraisalGrid, out var includesContents))
            return false;

        if (includesContents || !TryComp<ContainerManagerComponent>(uid, out var containers))
            return true;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var child in container.ContainedEntities)
            {
                // Solution entities have no prototype and are already included by PricingSystem.
                if (HasComp<Content.Shared.Chemistry.Components.SolutionComponent>(child))
                    continue;

                if (!AddEntity(child, state, depth + 1, appraisalGrid))
                    return false;
            }
        }

        return true;
    }

    private bool AddEntityOwn(EntityUid uid, BuildState state, int depth, EntityUid? appraisalGrid,
        out bool includesContents)
    {
        includesContents = false;
        if (!Visit(state, depth) || Deleted(uid) || !TryComp(uid, out MetaDataComponent? meta) ||
            meta.EntityPrototype is not { } prototype)
        {
            return state.Fail("Cannot appraise an invalid or unprototyped market entity.");
        }

        if (!state.Entities.Add(uid))
            return state.Fail($"Cyclic or duplicate market contents at {uid}.");

        var ignoreModifier = HasComp<Content.Server._NF.Cargo.Components.IgnoreMarketModifierComponent>(uid);
        if (TryComp<SpawnItemsOnUseComponent>(uid, out var package))
        {
            // Validate the graph before the legacy handled event, which spawns payloads to appraise them.
            // Deterministic packages can use the same commodities directly without any appraisal spawns.
            var payload = new BuildState();
            if (package.Uses <= 0 || !AddEntries(package.Items, package.Uses, payload, depth + 1))
                return state.Fail(payload.Failure ?? $"Empty use-spawner {prototype.ID} cannot be appraised.");

            state.Visited += payload.Visited;
            if (state.Visited > MaxVisitedNodes)
                return state.Fail("Market basket exceeds the bounded traversal limit.");

            if (payload.Exact)
            {
                includesContents = true;
                foreach (var line in payload.Lines)
                {
                    if (!AddLine(state, line with { SourceEntity = uid }))
                        return false;
                }
                return true;
            }
        }

        var tax = GetEntityTax(uid);
        var price = appraisalGrid is { } grid
            ? _pricing.GetPriceWithVendingDiscount(uid, grid, out var handled, includeContents: false)
            : _pricing.GetPrice(uid, out handled, includeContents: false);
        if (!double.IsFinite(price))
            return state.Fail($"Non-finite runtime appraisal for {prototype.ID}.");

        if (handled)
        {
            // Runtime handled events own the entire subtree. Random packages retain their actual,
            // opaque payout here; their prototype upper bounds also cover this wrapper commodity.
            includesContents = true;
            return AddLine(state, new MarketBasketLine(prototype.ID, DynamicMarketSystem.ProtoKey(prototype.ID),
                Math.Max(0, price), 1, ignoreModifier, uid, Tax: tax));
        }

        GasMixture? air = null;
        if (TryComp<GasCanisterComponent>(uid, out var canister))
            air = canister.Air;
        else if (TryComp<GasTankComponent>(uid, out var tank))
            air = tank.Air;

        if (air != null)
        {
            price -= _atmos.GetPrice(air);
            double total = 0;
            double largest = 0;
            for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
            {
                var moles = air.GetMoles(i);
                if (!float.IsFinite(moles) || moles < 0)
                    return state.Fail($"Invalid live gas mixture in {prototype.ID}.");
                total += moles;
                largest = Math.Max(largest, moles);
            }
            if (!AddGas(prototype.ID, air, 1, total > 0 ? largest / total : 1, ignoreModifier, state, uid))
                return false;
        }

        if (TryComp<BallisticAmmoProviderComponent>(uid, out var ammo))
        {
            if (!ammo.InfiniteUnspawned && ammo.Proto is { } ammoId && ammo.UnspawnedCount > 0)
            {
                if (!_prototypes.TryIndex(ammoId, out var ammoPrototype))
                    return state.Fail($"Unknown ammunition {ammoId}.");

                price -= _pricing.GetEstimatedPrice(ammoPrototype) * ammo.UnspawnedCount;
                if (!AddPrototype(ammoId, ammo.UnspawnedCount, state, depth + 1))
                    return false;
            }
        }

        if (TryComp<MaterialStorageComponent>(uid, out var materials))
        {
            foreach (var (id, amount) in materials.Storage)
            {
                if (!_prototypes.TryIndex(id, out var material))
                    return state.Fail($"Unknown stored material {id}.");
                price -= material.Price * amount;
            }
            if (!AddStoredMaterials(materials, 1, ignoreModifier, state))
                return false;
        }

        if (TryComp<RevolverAmmoProviderComponent>(uid, out var revolver) && revolver.FillPrototype is { } cartridge)
        {
            var unspawned = 0;
            if (revolver.Chambers.Length > MaxVisitedNodes)
                return state.Fail($"Excessive revolver capacity in {prototype.ID}.");
            for (var i = 0; i < revolver.Chambers.Length; i++)
            {
                // A chamber can contain either a spawned entity or an unspawned live/spent cartridge.
                if (revolver.Chambers[i] != null && (i >= revolver.AmmoSlots.Count || revolver.AmmoSlots[i] == null))
                    unspawned++;
            }
            if (!AddPrototype(cartridge, unspawned, state, depth + 1))
                return false;
        }

        // An emptied magazine/machine can still have the nominal fallback of its own shell.
        price = _fallback.ApplyFallback(uid, Math.Max(0, price));
        var units = TryComp<StackComponent>(uid, out var stack) ? Math.Max(0, stack.Count) : 1;
        var key = stack != null && air == null
            ? DynamicMarketSystem.StackKey(stack.StackTypeId)
            : DynamicMarketSystem.ProtoKey(prototype.ID);
        if (units > 0 && !AddLine(state, new MarketBasketLine(prototype.ID, key, price / units, units, ignoreModifier, uid,
                Tax: tax)))
            return false;

        return true;
    }
}
