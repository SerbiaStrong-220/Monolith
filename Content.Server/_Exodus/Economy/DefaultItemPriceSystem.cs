// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Construction.Components;
using Content.Shared.Item;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Fallback appraisal for items whose aggregate appraisal is effectively zero.
/// Explicit zero prices intentionally receive a fallback so rare loot is not worthless.
/// Already-priced entities keep their normal appraisal.
/// Explicit material and chemical composition is a minimum fallback value.
/// Lathe material costs are cached once per prototype reload.
/// </summary>
public sealed partial class DefaultItemPriceSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IComponentFactory _factory = default!;

    /// <summary>
    /// Cached material cost per output unit, including recipe batches and spawned stack counts.
    /// </summary>
    private readonly Dictionary<EntProtoId, double> _latheCraftCost = new();

    private bool _cacheBuilt;

    public override void Initialize()
    {
        base.Initialize();
        _prototypes.PrototypesReloaded += OnPrototypesReloaded;
        BuildLatheCache();
    }

    public override void Shutdown()
    {
        _prototypes.PrototypesReloaded -= OnPrototypesReloaded;
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<EntityPrototype>() || args.WasModified<LatheRecipePrototype>() ||
            args.WasModified<MaterialPrototype>())
            BuildLatheCache();
    }

    /// <summary>
    /// If <paramref name="currentPrice"/> is effectively zero, returns a sensible fallback.
    /// Otherwise returns <paramref name="currentPrice"/> unchanged (hot path for priced items).
    /// </summary>
    public double ApplyFallback(EntityUid uid, double currentPrice)
    {
        if (Math.Abs(currentPrice) > 0.01)
            return currentPrice;

        if (!TryComp<ItemComponent>(uid, out var item))
            return currentPrice;

        var fallback = EstimateFallback((uid, item));
        return Math.Max(currentPrice, fallback);
    }

    public double ApplyFallback(EntityPrototype prototype, double currentPrice)
    {
        if (Math.Abs(currentPrice) > 0.01)
            return currentPrice;

        if (!prototype.Components.ContainsKey(_factory.GetComponentName<ItemComponent>()))
            return currentPrice;

        return Math.Max(currentPrice, EstimateFallback(prototype));
    }

    private double EstimateFallback(Entity<ItemComponent> ent)
    {
        EnsureCache();
        var count = TryComp<StackComponent>(ent, out var stack) ? Math.Max(0, stack.Count) : 1;
        var prototype = MetaData(ent).EntityPrototype;
        var compositionPrice = GetCompositionPrice(CompOrNull<PhysicalCompositionComponent>(ent));

        // Machine parts: scale hard with rating (bluespace tier is expensive).
        if (TryComp<MachinePartComponent>(ent, out var part))
        {
            return Math.Max(MachinePartUnitPrice(part.Rating, prototype), compositionPrice) * count;
        }

        if (prototype != null && _latheCraftCost.TryGetValue(prototype.ID, out var craft))
        {
            // A fixed minimum here would let cheap batch recipes sell above their material cost.
            return Math.Max(craft * 0.65, compositionPrice) * count;
        }

        return Math.Max(ItemSizeFloor(ent.Comp.Size), compositionPrice) * count;
    }

    private double EstimateFallback(EntityPrototype prototype)
    {
        EnsureCache();
        var count = prototype.TryGetComponent<StackComponent>(out var stack, _factory) ? Math.Max(0, stack.Count) : 1;
        prototype.TryGetComponent<PhysicalCompositionComponent>(out var composition, _factory);
        var compositionPrice = GetCompositionPrice(composition);

        if (prototype.TryGetComponent<MachinePartComponent>(out var part, _factory))
            return Math.Max(MachinePartUnitPrice(part.Rating, prototype), compositionPrice) * count;

        if (_latheCraftCost.TryGetValue(prototype.ID, out var craft))
            return Math.Max(craft * 0.65, compositionPrice) * count;

        if (prototype.TryGetComponent<ItemComponent>(out var item, _factory))
            return Math.Max(ItemSizeFloor(item.Size), compositionPrice) * count;

        return count;
    }

    private double GetCompositionPrice(PhysicalCompositionComponent? composition)
    {
        if (composition == null)
            return 0;

        double materialPrice = 0;
        foreach (var (material, amount) in composition.MaterialComposition)
        {
            if (_prototypes.TryIndex(material, out MaterialPrototype? prototype))
                materialPrice += amount * prototype.Price;
        }

        double chemicalPrice = 0;
        foreach (var (reagent, amount) in composition.ChemicalComposition)
        {
            if (_prototypes.TryIndex(reagent, out ReagentPrototype? prototype))
                chemicalPrice += amount.Double() * prototype.PricePerUnit;
        }

        return materialPrice + chemicalPrice;
    }

    private double MachinePartUnitPrice(int rating, EntityPrototype? prototype)
    {
        // rating 1 → 150, 3 → ~1350, 4 → 2400, 6 bluespace → 5400
        rating = Math.Max(1, rating);
        var price = 150.0 * rating * rating;
        if (prototype != null && _latheCraftCost.TryGetValue(prototype.ID, out var craft))
            price = Math.Min(price, craft * 0.65);

        return price;
    }

    private double ItemSizeFloor(ProtoId<ItemSizePrototype> size)
    {
        if (!_prototypes.TryIndex(size, out ItemSizePrototype? sizeProto))
            return 1.0;

        // Size alone is not evidence of value. Use nominal prices rather than assigning high
        // appraisals to unpriced ammunition, paper and other supplies based only on their dimensions.
        return Math.Clamp(sizeProto.Weight, 1, 25);
    }

    private void EnsureCache()
    {
        if (!_cacheBuilt)
            BuildLatheCache();
    }

    private void BuildLatheCache()
    {
        _latheCraftCost.Clear();

        foreach (var recipe in _prototypes.EnumeratePrototypes<LatheRecipePrototype>())
        {
            if (recipe.Result is not { } resultProto || recipe.ResultCount <= 0)
                continue;

            double cost = 0;
            foreach (var (material, amount) in recipe.Materials)
            {
                if (!_prototypes.TryIndex(material, out MaterialPrototype? mat))
                    continue;
                cost += mat.Price * amount;
            }

            if (cost <= 0)
                continue;

            var units = (double)recipe.ResultCount;
            if (_prototypes.TryIndex(resultProto, out var prototype) &&
                prototype.TryGetComponent<StackComponent>(out var stack, _factory))
            {
                units *= Math.Max(1, stack.Count);
            }
            cost /= units;

            // Use the cheapest recipe so the fallback cannot exceed the actual crafting route.
            if (!_latheCraftCost.TryGetValue(resultProto, out var existing) || cost < existing)
                _latheCraftCost[resultProto] = cost;
        }

        _cacheBuilt = true;
        Log.Info($"Default item price: cached lathe craft costs for {_latheCraftCost.Count} results.");
    }
}
