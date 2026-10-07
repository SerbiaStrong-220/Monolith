// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._NF.Market.Components;
using Content.Server._Exodus.Shipyard;
using Content.Server.Cargo.Systems;
using Content.Server.Construction.Components;
using Content.Server.Station.Systems;
using Content.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Materials;
using Content.Shared.Mind.Components;
using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Receives completed sales from all endpoints, applies commodity pressure once and stores reproducible items.
/// Live entities and player state never enter resale stock.
/// </summary>
public sealed partial class MarketStockIntakeSystem : EntitySystem
{
    [Dependency] private MarketInventorySystem _inventory = default!;
    [Dependency] private StationSystem _stations = default!;
    [Dependency] private EntityWhitelistSystem _whitelists = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;
    [Dependency] private MarketBasketSystem _baskets = default!;
    [Dependency] private DynamicMarketSystem _market = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EntitySoldEvent>(OnPalletSale);
        SubscribeLocalEvent<MarketGoodsSoldEvent>(OnGoodsSold);
    }

    private void OnPalletSale(ref EntitySoldEvent args)
    {
        Receive(args.Sold, args.Grid, args.MarketImpactAppliedRoots);
    }

    private void OnGoodsSold(ref MarketGoodsSoldEvent args)
    {
        if (args.OriginalShip is { } ship &&
            TryComp<ShipyardOriginalContentsComponent>(ship, out var original))
        {
            ReceiveShipSale(args.Sold, args.Source, original);
            return;
        }

        Receive(args.Sold, args.Source);
    }

    private void Receive(IReadOnlyCollection<EntityUid> sold, EntityUid source,
        IReadOnlySet<EntityUid>? marketImpactAppliedRoots = null)
    {
        if (sold.Count == 0 || !_market.Ready)
            return;

        var station = _stations.GetOwningStation(source);
        var policy = TryComp<CargoMarketDataComponent>(station, out var local)
            ? local
            : _inventory.GetDefaultAdmissionRules();
        var roots = new HashSet<EntityUid>(sold);
        var transaction = new MarketTransactionState();
        var pending = new Stack<(EntityUid Uid, bool StockSuppressed, bool ImpactSuppressed)>();
        foreach (var root in roots)
        {
            if (!HasSoldAncestor(root, roots))
                pending.Push((root, false, false));
        }

        // An iterative traversal handles nested containers without recursion or a world-wide query.
        while (pending.TryPop(out var entry))
        {
            var (uid, stockSuppressed, impactSuppressed) = entry;
            if (TerminatingOrDeleted(uid) || HasComp<MarketStockProcessedComponent>(uid))
                continue;

            AddComp<MarketStockProcessedComponent>(uid);
            var protectedEntity = HasComp<ActorComponent>(uid) ||
                                  TryComp<MindContainerComponent>(uid, out var mind) && mind.HasMind;
            stockSuppressed |= protectedEntity;
            impactSuppressed |= protectedEntity || marketImpactAppliedRoots?.Contains(uid) == true;

            // Capture gas, ammunition and stored materials before stock intake consumes material storage.
            // A normal pallet already committed its recursive basket; fixed-reward turn-ins have not.
            if (!impactSuppressed)
                impactSuppressed = ApplySaleImpact(uid, transaction);

            var stockable = !stockSuppressed && CanStockEntity(policy, uid);
            if (!stockSuppressed && TryComp<MaterialStorageComponent>(uid, out var materials))
                ReceiveMaterials((uid, materials));
            if (stockable)
                ReceiveItem(uid);

            if (!TryComp<ContainerManagerComponent>(uid, out var containers))
                continue;

            foreach (var container in containers.Containers.Values)
            {
                // A purchased machine already recreates its installed board, parts and starting contents.
                var suppressContents = stockSuppressed || stockable && RecreatesContainer(uid, container);
                foreach (var child in container.ContainedEntities)
                    pending.Push((child, suppressContents, impactSuppressed));
            }
        }

        _market.CommitTransaction(transaction);
    }

    /// <returns>Whether the entity's appraisal already accounts for its entire subtree.</returns>
    private bool ApplySaleImpact(EntityUid uid, MarketTransactionState transaction)
    {
        if (!_baskets.TryGetEntityOwnBasket(uid, out var basket, out var includesContents, out _) || !basket.Exact)
            return false;

        foreach (var line in basket.Lines)
        {
            _market.CalculateSequentialSellValue(line.MarketKey, line.UnitBasePrice, line.Quantity,
                1, 1, transaction, applyImpact: false);
        }

        return includesContents;
    }

    private bool HasSoldAncestor(EntityUid uid, HashSet<EntityUid> roots)
    {
        var transforms = GetEntityQuery<TransformComponent>();
        while (transforms.TryGetComponent(uid, out var transform) && transform.ParentUid.IsValid())
        {
            uid = transform.ParentUid;
            if (roots.Contains(uid))
                return true;
        }

        return false;
    }

    private bool RecreatesContainer(EntityUid uid, BaseContainer container)
    {
        if (TryComp<MachineComponent>(uid, out var machine) &&
            (container == machine.BoardContainer || container == machine.PartContainer))
            return true;

        // ConstructionSystem.CreateComputerBoard uses this container for the prototype's board.
        if (container.ID == "board" && TryComp<ComputerComponent>(uid, out var computer) &&
            computer.BoardPrototype != null)
            return true;

        if (TryPrototype(uid, out var prototype) &&
            prototype.TryGetComponent<ContainerFillComponent>(out var fill, Factory) &&
            fill.Containers.ContainsKey(container.ID))
            return true;

        if (prototype != null &&
            prototype.TryGetComponent<EntityTableContainerFillComponent>(out var tableFill, Factory))
        {
            foreach (var (id, _) in tableFill.Containers)
            {
                if (id == container.ID)
                    return true;
            }
        }

        if (TryComp<ItemSlotsComponent>(uid, out var slots))
        {
            foreach (var slot in slots.Slots.Values)
            {
                if (slot.ContainerSlot == container && slot.StartingItem != null)
                    return true;
            }
        }

        return false;
    }

    private bool CanStockEntity(CargoMarketDataComponent policy, EntityUid uid)
    {
        if ((_whitelists.IsWhitelistPassOrNull(policy.Whitelist, uid) &&
             _whitelists.IsBlacklistFailOrNull(policy.Blacklist, uid)) ||
            _whitelists.IsWhitelistPass(policy.WhitelistOverride, uid))
            return true;

        foreach (var rule in policy.AdditionalStockRules)
        {
            if (_whitelists.IsWhitelistPass(rule.Whitelist, uid) &&
                _whitelists.IsBlacklistFailOrNull(rule.Blacklist, uid))
                return true;
        }

        return false;
    }

    private void ReceiveItem(EntityUid uid)
    {
        if (!TryPrototype(uid, out var prototype))
            return;

        var count = 1;
        string? stackId = null;
        if (TryComp<StackComponent>(uid, out var stack))
        {
            count = stack.Count;
            stackId = stack.StackTypeId;
            if (!_prototypes.TryIndex<StackPrototype>(stackId, out var stackPrototype) ||
                !_prototypes.TryIndex(stackPrototype.Spawn, out prototype))
                return;
        }

        if (count <= 0)
            return;

        var price = _pricing.GetPrice(uid, includeContents: false) / count;
        _inventory.TryAddStock(prototype.ID, count, price, stackId);
    }

    private void ReceiveMaterials(Entity<MaterialStorageComponent> storage)
    {
        foreach (var (materialId, amount) in storage.Comp.Storage)
        {
            if (amount <= 0 || !_prototypes.TryIndex(materialId, out var material) ||
                material.StackEntity is not { } stackEntity ||
                !_prototypes.TryIndex(stackEntity, out var prototype) ||
                !prototype.TryGetComponent<PhysicalCompositionComponent>(out var composition, Factory) ||
                !prototype.TryGetComponent<StackComponent>(out var stack, Factory) ||
                !composition.MaterialComposition.TryGetValue(material.ID, out var perUnit) || perUnit <= 0)
                continue;

            var units = amount / perUnit;
            if (units == 0)
                continue;

            var consumed = units * perUnit;
            if (!_materials.CanChangeMaterialAmount(storage, materialId, -consumed, storage.Comp, localOnly: true) ||
                !_inventory.TryAddStock(prototype.ID, units, material.Price * perUnit, stack.StackTypeId))
                continue;

            _materials.TryChangeMaterialAmount(storage, materialId, -consumed, storage.Comp, localOnly: true);
        }
    }
}
