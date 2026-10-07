// (c) Space Exodus Team - EXDS-RL with CLA
using System.Diagnostics.CodeAnalysis;
using Content.Server._NF.Market.Components;
using Content.Shared._NF.Market;
using Content.Shared.GameTicking;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// Owns the round's shared resale inventory. All operations run synchronously on the simulation thread.
/// Stock is taken only when a purchase is paid, never while filling a terminal's cart.
/// </summary>
public sealed partial class MarketInventorySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly TimeSpan NotificationInterval = TimeSpan.FromMilliseconds(250);
    private static readonly EntProtoId InventoryPrototype = "ExodusMarketInventory";
    private Entity<MarketInventoryComponent>? _inventory;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_inventory is not { } cached || !cached.Comp.Changed ||
            _timing.CurTime < cached.Comp.NextNotification || !TryGetInventory(out var inventory))
            return;

        inventory.Comp.Changed = false;
        inventory.Comp.NextNotification = _timing.CurTime + NotificationInterval;
        var ev = new MarketInventoryChangedEvent();
        RaiseLocalEvent(ref ev);
    }

    /// <summary>Returns detached entries; callers cannot change stock through a listing.</summary>
    public IReadOnlyList<MarketData> GetStock()
    {
        if (!TryGetInventory(out var inventory) || inventory.Comp.Stock.Count == 0)
            return Array.Empty<MarketData>();

        var snapshot = new MarketData[inventory.Comp.Stock.Count];
        var index = 0;
        foreach (var entry in inventory.Comp.Stock.Values)
            snapshot[index++] = Snapshot(entry);
        return snapshot;
    }

    public bool TryGetStock(EntProtoId prototype, [NotNullWhen(true)] out MarketData? stock)
    {
        stock = null;
        if (!TryGetInventory(out var inventory) || FindStock(inventory, prototype) is not { } entry)
            return false;

        stock = Snapshot(entry);
        return true;
    }

    public bool TryAddStock(EntProtoId prototype, int amount, double unitPrice, string? stackPrototype = null)
    {
        if (amount <= 0 || !double.IsFinite(unitPrice) || unitPrice < 0 ||
            !ValidatePrototype(prototype, stackPrototype))
            return false;

        var inventory = GetOrCreateInventory();
        var entry = FindStock(inventory, prototype);
        if (entry == null)
        {
            inventory.Comp.Stock.Add(prototype, new MarketData(prototype, stackPrototype, amount, unitPrice));
        }
        else
        {
            if (entry.StackPrototype?.Id != stackPrototype || entry.Quantity <= 0 ||
                !double.IsFinite(entry.Price) || entry.Price < 0)
                return false;

            var quantity = (long) entry.Quantity + amount;
            if (quantity > int.MaxValue)
                return false;

            var addedShare = amount / (double) quantity;
            var price = entry.Price * (1 - addedShare) + unitPrice * addedShare;
            if (!double.IsFinite(price))
                return false;

            entry.Quantity = (int) quantity;
            entry.Price = price;
        }

        inventory.Comp.Changed = true;
        return true;
    }

    public bool TryTakeStock(EntProtoId prototype, int amount, [NotNullWhen(true)] out MarketData? taken)
    {
        taken = null;
        if (amount <= 0 || !TryGetInventory(out var inventory) ||
            FindStock(inventory, prototype) is not { } entry || entry.Quantity < amount)
            return false;

        taken = new MarketData(entry.Prototype, entry.StackPrototype, amount, entry.Price);
        TakeStock(inventory, entry, amount);
        return true;
    }

    /// <summary>
    /// Takes the entire requested basket or leaves every entry untouched. Duplicate products are rejected.
    /// Returned prices always come from the inventory, not from the supplied request snapshots.
    /// </summary>
    public bool TryTakeStock(IReadOnlyList<MarketData> requests, [NotNullWhen(true)] out List<MarketData>? taken)
    {
        taken = null;
        if (requests.Count == 0 || !TryGetInventory(out var inventory))
            return false;

        var products = new HashSet<EntProtoId>();
        var entries = new List<MarketData>(requests.Count);
        var snapshots = new List<MarketData>(requests.Count);
        foreach (var request in requests)
        {
            if (request.Quantity <= 0 || !double.IsFinite(request.Price) || request.Price < 0 ||
                !products.Add(request.Prototype) || FindStock(inventory, request.Prototype) is not { } entry ||
                entry.Quantity < request.Quantity || entry.StackPrototype != request.StackPrototype)
                return false;

            entries.Add(entry);
            snapshots.Add(new MarketData(entry.Prototype, entry.StackPrototype, request.Quantity, entry.Price));
        }

        for (var i = 0; i < entries.Count; i++)
            TakeStock(inventory, entries[i], snapshots[i].Quantity);

        taken = snapshots;
        return true;
    }

    public void Clear()
    {
        if (!TryGetInventory(out var inventory) || inventory.Comp.Stock.Count == 0)
            return;

        inventory.Comp.Stock.Clear();
        inventory.Comp.Changed = true;
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        Clear();
    }

    private bool ValidatePrototype(EntProtoId prototype, string? stackId)
    {
        if (string.IsNullOrWhiteSpace(prototype.Id) || !_prototypes.TryIndex(prototype, out var entity) || entity.Abstract)
            return false;

        if (!entity.TryGetComponent<StackComponent>(out var stack, Factory))
            return stackId == null;

        return stackId != null && stack.StackTypeId == stackId &&
               _prototypes.TryIndex<StackPrototype>(stackId, out var stackPrototype) &&
               _prototypes.TryIndex(stackPrototype.Spawn, out var spawned) && !spawned.Abstract &&
               spawned.TryGetComponent<StackComponent>(out var spawnedStack, Factory) &&
               spawnedStack.StackTypeId == stackId;
    }

    private Entity<MarketInventoryComponent> GetOrCreateInventory()
    {
        if (TryGetInventory(out var inventory))
            return inventory;

        var uid = Spawn(InventoryPrototype, MapCoordinates.Nullspace);
        inventory = new Entity<MarketInventoryComponent>(uid, Comp<MarketInventoryComponent>(uid));
        _inventory = inventory;
        return inventory;
    }

    private bool TryGetInventory(out Entity<MarketInventoryComponent> inventory)
    {
        if (_inventory is { } cached && cached.Comp.Initialized && !cached.Comp.Deleted &&
            !TerminatingOrDeleted(cached.Owner))
        {
            inventory = cached;
            return true;
        }

        _inventory = null;
        inventory = default;
        return false;
    }

    internal CargoMarketDataComponent GetDefaultAdmissionRules()
    {
        return Comp<CargoMarketDataComponent>(GetOrCreateInventory());
    }

    private static MarketData? FindStock(Entity<MarketInventoryComponent> inventory, EntProtoId prototype)
    {
        return inventory.Comp.Stock.GetValueOrDefault(prototype);
    }

    private static MarketData Snapshot(MarketData entry)
    {
        return new MarketData(entry.Prototype, entry.StackPrototype, entry.Quantity, entry.Price);
    }

    private static void TakeStock(Entity<MarketInventoryComponent> inventory, MarketData entry, int amount)
    {
        entry.Quantity -= amount;
        if (entry.Quantity == 0)
            inventory.Comp.Stock.Remove(entry.Prototype);
        inventory.Comp.Changed = true;
    }
}
