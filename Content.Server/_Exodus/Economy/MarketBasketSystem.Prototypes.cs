// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server.Light.Components;
using Content.Server.Spawners.Components;
using Content.Server.Storage.Components;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Atmos.Piping.Unary.Components;
using Content.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Materials;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketBasketSystem
{
    private bool AddPrototype(EntProtoId id, double count, BuildState state, int depth)
    {
        if (!Visit(state, depth) || !double.IsFinite(count) || count < 0 || count > MaxQuantity)
            return state.Fail($"Invalid or excessive delivery count for {id}.");

        if (count == 0)
            return true;

        if (!_prototypes.TryIndex(id, out var prototype))
            return state.Fail($"Unknown delivery prototype {id}.");

        if (!state.Prototypes.Add(id))
            return state.Fail($"Cyclic delivery prototype {id}.");

        try
        {
            var firstLine = state.Lines.Count;
            if (!AddPrototypeContents(prototype, count, state, depth))
                return false;

            if (prototype.TryGetComponent<StackComponent>(out var stack, _factory) &&
                (state.Lines.Count != firstLine + 1 ||
                 state.Lines[firstLine].MarketKey != DynamicMarketSystem.StackKey(stack.StackTypeId) ||
                 prototype.Components.ContainsKey("SpawnItemsOnUse")))
            {
                return state.Fail($"Composite stack {prototype.ID} requires a split-contents basket adapter.");
            }

            return true;
        }
        finally
        {
            state.Prototypes.Remove(id);
        }
    }

    private bool AddPrototypeContents(EntityPrototype prototype, double count, BuildState state, int depth)
    {
        // Spawners disappear rather than becoming the delivered commodity.
        if (prototype.TryGetComponent<RandomSpawnerComponent>(out var random, _factory))
        {
            if (!random.DeleteSpawnerAfterSpawn)
                return state.Fail($"Reusable random spawner {prototype.ID} requires a basket adapter.");

            state.Exact = false;
            foreach (var child in random.Prototypes)
            {
                if (!AddPrototype(child, count, state, depth + 1))
                    return false;
            }
            foreach (var child in random.RarePrototypes)
            {
                if (!AddPrototype(child, count, state, depth + 1))
                    return false;
            }
            return true;
        }

        if (prototype.TryGetComponent<EntityTableSpawnerComponent>(out var spawner, _factory))
        {
            if (!spawner.DeleteSpawnerAfterSpawn)
                return state.Fail($"Reusable table spawner {prototype.ID} requires a basket adapter.");

            return AddSelector(spawner.Table, count, state, depth + 1);
        }

        if (prototype.TryGetComponent<ConditionalSpawnerComponent>(out _, _factory))
            return state.Fail($"Conditional spawner {prototype.ID} requires a basket adapter.");

        // PriceCalculationEvent treats unopened packages as their payload, including its contents.
        if (prototype.TryGetComponent<SpawnItemsOnUseComponent>(out var package, _factory))
        {
            if (package.Uses <= 0)
                return state.Fail($"Empty use-spawner {prototype.ID} has an ambiguous runtime appraisal.");

            var firstLine = state.Lines.Count;
            var firstNestedPackage = state.Packages?.Count ?? 0;
            var previouslyExact = state.Exact;
            state.Exact = true;
            if (!AddEntries(package.Items, count * package.Uses, state, depth + 1))
                return false;

            var payloadExact = state.Exact;
            state.Exact &= previouslyExact;
            if (payloadExact)
            {
                (state.Packages ??= new()).Add(new MarketBasketPackage(prototype.ID, count, package.Uses,
                    firstLine, state.Lines.Count - firstLine, firstNestedPackage));
                return true;
            }

            // A random unopened package is appraised by its expected payload under the wrapper key.
            // Include that alternative in the upper bound as well as all possible unpacked commodities.
            double maximumPayload = 0;
            for (var i = firstLine; i < state.Lines.Count; i++)
                maximumPayload += state.Lines[i].Quantity * state.Lines[i].UnitBasePrice;
            return AddLine(state, new MarketBasketLine(prototype.ID, DynamicMarketSystem.ProtoKey(prototype.ID),
                maximumPayload / count, count, prototype.Components.ContainsKey("IgnoreMarketModifier"),
                Tax: GetPrototypeTax(prototype)));
        }

        if (!TryGetPrototypeOwnPrice(prototype, out var ownPrice, state))
            return false;

        var ignoreModifier = prototype.Components.ContainsKey("IgnoreMarketModifier");
        var units = prototype.TryGetComponent<StackComponent>(out var stack, _factory) ? stack.Count : 1;
        if (units <= 0)
            return state.Fail($"Invalid stack count in {prototype.ID}.");

        GasMixture? air = null;
        if (prototype.TryGetComponent<GasCanisterComponent>(out var canister, _factory))
            air = canister.Air;
        else if (prototype.TryGetComponent<GasTankComponent>(out var tank, _factory))
            air = tank.Air;

        var key = stack != null && air == null
            ? DynamicMarketSystem.StackKey(stack.StackTypeId)
            : DynamicMarketSystem.ProtoKey(prototype.ID);
        double? resaleUnitPrice = null;
        if (stack != null && !TryGetStackResaleUnitPrice(prototype, stack, state, out resaleUnitPrice))
            return false;
        if (!AddLine(state, new MarketBasketLine(prototype.ID, key, ownPrice / units, units * count,
                ignoreModifier, ResaleUnitPrice: resaleUnitPrice, Tax: GetPrototypeTax(prototype),
                ResaleTaxMultiplier: stack == null ? null : _stackTaxMultipliers.GetValueOrDefault(stack.StackTypeId, 1))))
            return false;

        // Gas deposit consoles can sell each species without the pallet's mixture-purity penalty.
        if (air != null && !AddGas(prototype.ID, air, count, 1, ignoreModifier, state))
            return false;

        if (prototype.TryGetComponent<BallisticAmmoProviderComponent>(out var ammo, _factory))
        {
            // Infinite virtual rounds are excluded from cycling and transfer by GunSystem.
            // Only finite initial rounds and actual contained entities are delivered commodities.
            if (!ammo.InfiniteUnspawned && ammo.Proto is { } ammoId &&
                !AddPrototype(ammoId, count * ammo.Capacity, state, depth + 1))
                return false;

            // MapInit order decides whether preloaded entities reduce unspawned ammunition.
            if (!ammo.InfiniteUnspawned && prototype.TryGetComponent<ContainerFillComponent>(out var ammoFill, _factory) &&
                ammoFill.Containers.ContainsKey("ballistic-ammo"))
            {
                state.Exact = false;
            }
            if (!ammo.InfiniteUnspawned && prototype.TryGetComponent<EntityTableContainerFillComponent>(out var ammoTable, _factory))
            {
                var tableContainers = ammoTable.Containers;
                if (tableContainers.ContainsKey("ballistic-ammo"))
                    state.Exact = false;
            }
        }

        if (prototype.TryGetComponent<MaterialStorageComponent>(out var materials, _factory) &&
            !AddStoredMaterials(materials, count, ignoreModifier, state))
        {
            return false;
        }

        if (prototype.TryGetComponent<RevolverAmmoProviderComponent>(out var revolver, _factory) &&
            revolver.FillPrototype is { } cartridge &&
            !AddPrototype(cartridge, count * revolver.Capacity, state, depth + 1))
        {
            return false;
        }

        if (prototype.TryGetComponent<StorageFillComponent>(out var fill, _factory) &&
            !AddEntries(fill.Contents, count, state, depth + 1))
        {
            return false;
        }

        if (prototype.TryGetComponent<ContainerFillComponent>(out var containers, _factory))
        {
            foreach (var contents in containers.Containers.Values)
            {
                foreach (var child in contents)
                {
                    if (!AddPrototype(child, count, state, depth + 1))
                        return false;
                }
            }
        }

        if (prototype.TryGetComponent<EntityTableContainerFillComponent>(out var tables, _factory))
        {
            foreach (var selector in tables.Containers.Values)
            {
                if (!AddSelector(selector, count, state, depth + 1))
                    return false;
            }
        }

        if (prototype.TryGetComponent<ItemSlotsComponent>(out var slots, _factory))
        {
            foreach (var slot in slots.Slots.Values)
            {
                if (slot.StartingItem is { } child && !AddPrototype(child, count, state, depth + 1))
                    return false;
            }
        }

        if (prototype.TryGetComponent<LightReplacerComponent>(out var replacer, _factory) &&
            !AddEntries(replacer.Contents, count, state, depth + 1))
        {
            return false;
        }

        if (prototype.TryGetComponent<SpawnTableOnUseComponent>(out var tablePackage, _factory))
        {
            // Opening consumes the wrapper; including its price is a conservative upper bound.
            state.Exact = false;
            return AddSelector(tablePackage.Table, count, state, depth + 1);
        }

        return AddGeneratedContents(prototype, count, state, depth);
    }

    private bool AddEntries(List<EntitySpawnEntry> entries, double count, BuildState state, int depth)
    {
        if (entries.Count > MaxVisitedNodes)
            return state.Fail("Market basket contains too many spawn entries.");

        var groups = new Dictionary<string, int>();
        foreach (var entry in entries)
        {
            if (!float.IsFinite(entry.SpawnProbability) || entry.SpawnProbability < 0 ||
                entry.Amount < 0 || entry.MaxAmount < 0)
            {
                return state.Fail("Invalid entity spawn entry in market basket.");
            }

            if (entry.SpawnProbability == 0)
                continue;

            if (!string.IsNullOrEmpty(entry.GroupId))
                groups[entry.GroupId] = groups.GetValueOrDefault(entry.GroupId) + 1;
            else if (entry.SpawnProbability < 1)
                state.Exact = false;

            if (entry.MaxAmount > entry.Amount)
                state.Exact = false;
        }

        foreach (var groupCount in groups.Values)
        {
            if (groupCount > 1)
                state.Exact = false;
        }

        foreach (var entry in entries)
        {
            if (entry.SpawnProbability <= 0 || entry.PrototypeId is not { } id)
                continue;

            // Include every possible branch. This is deliberately not a probabilistic expected value.
            if (!AddPrototype(id, count * Math.Max(entry.Amount, entry.MaxAmount), state, depth))
                return false;
        }

        return true;
    }

    private bool AddStoredMaterials(MaterialStorageComponent storage, double count, bool ignoreModifier, BuildState state)
    {
        foreach (var (id, amount) in storage.Storage)
        {
            if (amount == 0)
                continue;

            if (amount < 0 || !_prototypes.TryIndex(id, out var material) ||
                material.StackEntity is not { } stackId || !_prototypes.TryIndex(stackId, out var stackPrototype) ||
                !stackPrototype.TryGetComponent<StackComponent>(out var stack, _factory) ||
                !stackPrototype.TryGetComponent<PhysicalCompositionComponent>(out var composition, _factory) ||
                !composition.MaterialComposition.TryGetValue(id, out var perUnit) || perUnit <= 0)
            {
                return state.Fail($"Stored material {id} cannot be mapped to a commodity.");
            }

            if (!TryGetStackResaleUnitPrice(stackPrototype, stack, state, out var resaleUnitPrice) ||
                !AddLine(state, new MarketBasketLine(stackId, DynamicMarketSystem.StackKey(stack.StackTypeId),
                    material.Price * perUnit, count * amount / perUnit, ignoreModifier, ResaleUnitPrice: resaleUnitPrice,
                    Tax: GetPrototypeTax(stackPrototype),
                    ResaleTaxMultiplier: _stackTaxMultipliers.GetValueOrDefault(stack.StackTypeId, 1))))
            {
                return false;
            }
        }

        return true;
    }

    private bool TryGetStackResaleUnitPrice(EntityPrototype prototype, StackComponent stack,
        BuildState state, out double? price)
    {
        price = null;
        if (!_prototypes.TryIndex<StackPrototype>(stack.StackTypeId, out var stackType) ||
            !_prototypes.TryIndex(stackType.Spawn, out var spawned) ||
            !spawned.TryGetComponent<StackComponent>(out var spawnedStack, _factory) ||
            spawnedStack.StackTypeId != stack.StackTypeId)
        {
            return state.Fail($"Invalid split commodity for {prototype.ID}.");
        }

        // A canonical split spawn can generate contents even when the original variant does not.
        // Such stacks need a full split-composition adapter; an own-price bound is insufficient.
        foreach (var component in spawned.Components.Keys)
        {
            if (component is "StorageFill" or "ContainerFill" or "EntityTableContainerFill" or "ItemSlots" or
                "SpawnItemsOnUse" or "SpawnTableOnUse" or "OpenTriggeredStorageFill" or "RandomSpawner" or
                "EntityTableSpawner" or "ConditionalSpawner" or "Machine" or "Body" or "Bin" or "HandsFill" or
                "Loadout" or "LightReplacer" or "MaterialStorage" or "GasTank" or "GasCanister" or
                "BallisticAmmoProvider" or "RevolverAmmoProvider")
            {
                return state.Fail($"Composite split spawn {spawned.ID} requires a split-contents basket adapter.");
            }
        }

        if (!TryGetPrototypeOwnPrice(prototype, out var originalPrice, state, singleStack: true) ||
            !TryGetPrototypeOwnPrice(spawned, out var spawnedPrice, state, singleStack: true))
        {
            return false;
        }

        // Splitting recreates the canonical prototype while the final original unit keeps its own appraisal.
        price = Math.Max(originalPrice, spawnedPrice);
        return true;
    }
}
