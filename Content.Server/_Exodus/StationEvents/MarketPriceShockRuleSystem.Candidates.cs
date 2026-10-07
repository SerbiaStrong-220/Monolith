// (c) Space Exodus Team - EXDS-RL with CLA
using System.Globalization;
using Content.Server._Exodus.Economy;
using Content.Shared._Exodus.Economy;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Content.Shared.Cargo.Components;
using Content.Shared.Cargo.Prototypes;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Content.Shared.VendingMachines;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.StationEvents;

public sealed partial class MarketPriceShockRuleSystem
{
    private readonly record struct ShockCandidate(string MarketKey, EntProtoId? Prototype, int? GasId);

    private ShockCandidate[] _candidates = Array.Empty<ShockCandidate>();
    private readonly HashSet<ProtoId<MarketCommodityGroupPrototype>> _candidateGroups = new();
    private bool _candidatesDirty = true;

    private void InitializeCandidates()
    {
        PrototypeManager.PrototypesReloaded += OnCandidatePrototypesReloaded;
    }

    private void ShutdownCandidates()
    {
        PrototypeManager.PrototypesReloaded -= OnCandidatePrototypesReloaded;
    }

    private void OnCandidatePrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        // Classification and basket caches have their own reload subscriptions. Rebuild lazily
        // after all subscribers finish, without traversing baskets while rebuilding this pool.
        _candidatesDirty = true;
    }

    private ShockCandidate[] GetCandidates(IReadOnlySet<ProtoId<MarketCommodityGroupPrototype>> allowedGroups)
    {
        // Classification caches must be ready before filtering, including on the first use.
        if (_candidatesDirty || !_candidateGroups.SetEquals(allowedGroups))
            RebuildCandidates(allowedGroups);

        return _candidates;
    }

    private void RebuildCandidates(IReadOnlySet<ProtoId<MarketCommodityGroupPrototype>> allowedGroups)
    {
        var catalog = new HashSet<EntProtoId>();
        foreach (var product in PrototypeManager.EnumeratePrototypes<CargoProductPrototype>())
            catalog.Add(product.Product);

        foreach (var recipe in PrototypeManager.EnumeratePrototypes<LatheRecipePrototype>())
        {
            if (recipe.Result is { } result && recipe.ResultCount > 0)
                catalog.Add(result);
        }

        foreach (var inventory in PrototypeManager.EnumeratePrototypes<VendingMachineInventoryPrototype>())
        {
            AddInventory(inventory.StartingInventory, catalog);
            AddInventory(inventory.EmaggedInventory, catalog);
            AddInventory(inventory.ContrabandInventory, catalog);
        }

        var candidates = new Dictionary<string, ShockCandidate>(StringComparer.Ordinal);
        foreach (var prototype in PrototypeManager.EnumeratePrototypes<EntityPrototype>())
        {
            if (!IsCandidatePrototype(prototype) ||
                !catalog.Contains(prototype.ID) && !HasCandidatePricePotential(prototype))
                continue;

            var candidatePrototype = prototype;
            string key;
            // Gas containers retain their own shell key even if a custom prototype also stacks.
            if (!prototype.Components.ContainsKey("GasCanister") && !prototype.Components.ContainsKey("GasTank") &&
                prototype.TryGetComponent<StackComponent>(out var stack, EntityManager.ComponentFactory))
            {
                ProtoId<StackPrototype> stackId = stack.StackTypeId;
                if (!PrototypeManager.TryIndex(stackId, out var stackPrototype) ||
                    !PrototypeManager.TryIndex(stackPrototype.Spawn, out var spawnedPrototype) ||
                    !IsCandidatePrototype(spawnedPrototype))
                    continue;

                candidatePrototype = spawnedPrototype;
                key = DynamicMarketSystem.StackKey(stackPrototype.ID);
            }
            else
            {
                key = DynamicMarketSystem.ProtoKey(prototype.ID);
            }

            if (allowedGroups.Contains(_commodityGroups.GetGroup(key)))
                candidates.TryAdd(key, new ShockCandidate(key, candidatePrototype.ID, null));
        }

        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            // AtmosphereSystem indexes numeric gas prototype IDs, while market keys use enum names.
            ProtoId<GasPrototype> gasId = i.ToString(CultureInfo.InvariantCulture);
            if (!PrototypeManager.TryIndex(gasId, out var gas) || !float.IsFinite(gas.PricePerMole) || gas.PricePerMole <= 0)
                continue;

            var key = DynamicMarketSystem.GasKey(i);
            if (allowedGroups.Contains(_commodityGroups.GetGroup(key)))
                candidates.Add(key, new ShockCandidate(key, null, i));
        }

        var snapshot = new ShockCandidate[candidates.Count];
        candidates.Values.CopyTo(snapshot, 0);
        Array.Sort(snapshot, static (left, right) => string.CompareOrdinal(left.MarketKey, right.MarketKey));
        _candidates = snapshot;
        _candidateGroups.Clear();
        _candidateGroups.UnionWith(allowedGroups);
        _candidatesDirty = false;
    }

    private static void AddInventory(Dictionary<string, uint>? inventory, HashSet<EntProtoId> catalog)
    {
        if (inventory == null)
            return;

        foreach (var (prototype, count) in inventory)
        {
            if (count > 0)
                catalog.Add(prototype);
        }
    }

    private static bool IsCandidatePrototype(EntityPrototype prototype)
    {
        var components = prototype.Components;
        if (prototype.Abstract || components.ContainsKey("CargoSellBlacklist") ||
            components.ContainsKey("IgnoreMarketModifier") ||
            components.ContainsKey("MobState") && !components.ContainsKey("Mech") ||
            components.ContainsKey("RandomSpawner") || components.ContainsKey("EntityTableSpawner") ||
            components.ContainsKey("ConditionalSpawner"))
            return false;

        // Physical ammunition such as arrows can have Projectile and still be ordinary items.
        return components.ContainsKey("Item") ||
               !components.ContainsKey("Projectile") && !components.ContainsKey("EffectVisuals");
    }

    private bool HasCandidatePricePotential(EntityPrototype prototype)
    {
        // Unpriced items receive DefaultItemPriceSystem's fallback. Non-items need evidence of value.
        if (prototype.Components.ContainsKey("Item"))
            return true;

        if (prototype.TryGetComponent<StaticPriceComponent>(out var price, EntityManager.ComponentFactory) &&
            double.IsFinite(price.Price) && price.Price > 0)
            return true;

        if (!prototype.TryGetComponent<PhysicalCompositionComponent>(out var composition, EntityManager.ComponentFactory))
            return false;

        foreach (var amount in composition.MaterialComposition.Values)
        {
            if (amount > 0)
                return true;
        }

        foreach (var amount in composition.ChemicalComposition.Values)
        {
            if (amount > 0)
                return true;
        }

        return false;
    }

    private bool TryGetCandidateName(ShockCandidate candidate, out string name)
    {
        name = string.Empty;
        if (candidate.GasId is { } gasIndex)
        {
            ProtoId<GasPrototype> gasId = gasIndex.ToString(CultureInfo.InvariantCulture);
            if (!PrototypeManager.TryIndex(gasId, out var gas) || !float.IsFinite(gas.PricePerMole) || gas.PricePerMole <= 0)
                return false;

            name = Loc.GetString(gas.Name);
            return !string.IsNullOrWhiteSpace(name);
        }

        if (candidate.Prototype is not { } id || !PrototypeManager.TryIndex(id, out var prototype) ||
            !IsCandidatePrototype(prototype) || !_baskets.TryGetPrototypeBasket(id, out var basket, out _))
            return false;

        foreach (var line in basket.Lines)
        {
            // A wrapper replaced entirely by its payload has no independently traded own key.
            if (line.MarketKey != candidate.MarketKey || line.IgnoreMarketModifier ||
                line.Quantity <= 0 || line.UnitBasePrice <= 0)
                continue;

            name = prototype.Name;
            if (prototype.TryGetComponent<StackComponent>(out var stack, EntityManager.ComponentFactory) &&
                candidate.MarketKey == DynamicMarketSystem.StackKey(stack.StackTypeId))
            {
                ProtoId<StackPrototype> stackId = stack.StackTypeId;
                if (PrototypeManager.TryIndex(stackId, out var stackPrototype) &&
                    !string.IsNullOrWhiteSpace(stackPrototype.Name) && Loc.TryGetString(stackPrototype.Name, out var stackName))
                    name = stackName;
            }

            return !string.IsNullOrWhiteSpace(name);
        }

        return false;
    }
}
