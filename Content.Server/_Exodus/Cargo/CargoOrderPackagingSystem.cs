using Content.Server.Stack;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Cargo;
using Content.Shared.Construction.Components;
using Content.Shared.Item;
using Content.Shared.Stacks;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Cargo;

/// <summary>
/// Packs a delivery into an ordinary crate without changing the number of purchased goods.
/// Runs only when a cargo terminal dispatches an order.
/// </summary>
public sealed partial class CargoOrderPackagingSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private EntityStorageSystem _storage = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    /// <summary>
    /// Structures and NPC equipment travel inside a crate even for single-item orders.
    /// Existing storage containers are their own packaging.
    /// </summary>
    public bool RequiresPackaging(EntProtoId prototypeId)
    {
        return _prototypes.TryIndex(prototypeId, out var prototype) &&
               (prototype.TryGetComponent<AnchorableComponent>(out _, Factory) ||
                prototype.TryGetComponent<CargoSellableMobComponent>(out _, Factory)) &&
               !prototype.TryGetComponent<EntityStorageComponent>(out _, Factory);
    }

    /// <summary>
    /// Attempts to pack the already spawned first item and further units of the same order.
    /// On failure, the caller must discard the first item if packaging is required, otherwise it can deliver it directly.
    /// </summary>
    public bool TryPackOrder(EntityUid firstItem, CargoOrderData order, CargoOrderPackagingSettings settings,
        EntityCoordinates coordinates, out EntityUid package, out int delivered)
    {
        package = firstItem;
        delivered = 0;
        var remaining = order.OrderQuantity - order.NumDispatched;
        var required = RequiresPackaging(order.ProductId);
        if (remaining <= 0 || HasComp<EntityStorageComponent>(firstItem) ||
            !required && (settings.MinimumQuantity <= 0 || order.OrderQuantity < settings.MinimumQuantity ||
                          !HasComp<ItemComponent>(firstItem)))
        {
            return false;
        }

        if (!_prototypes.HasIndex(settings.CratePrototype))
        {
            Log.Error($"Unknown cargo packaging crate: {settings.CratePrototype}.");
            return false;
        }

        var crate = Spawn(settings.CratePrototype, coordinates);
        if (!TryComp<EntityStorageComponent>(crate, out var storage) || storage.Open ||
            storage.Capacity <= 0 || storage.Contents.ContainedEntities.Count != 0 ||
            !required && !_storage.CanInsert(firstItem, crate, storage))
        {
            QueueDel(crate);
            return false;
        }

        _transform.Unanchor(crate, Transform(crate));

        var stackQuery = GetEntityQuery<StackComponent>();
        var item = firstItem;
        while (delivered < remaining)
        {
            // Check capacity before spawning another item. The remainder stays in the paid order.
            if (storage.Contents.ContainedEntities.Count >= storage.Capacity)
                break;

            if (delivered > 0)
            {
                item = required ? Spawn(order.ProductId) : Spawn(order.ProductId, coordinates);
                _transform.Unanchor(item, Transform(item));
            }

            var count = 1;
            var originalCount = 0;
            if (stackQuery.TryGetComponent(item, out var stack))
            {
                if (order.FromResaleStock)
                    _stack.SetCount(item, 1, stack);

                originalCount = stack.Count;
                if (originalCount > 0 && !stack.Unlimited)
                {
                    // Catalog quantities count whole prototype stacks; resale quantities count units.
                    // Use full stacks where possible instead of spawning and deleting one entity per unit.
                    count = Math.Min(remaining - delivered, Math.Max(1, _stack.GetMaxCount(stack) / originalCount));
                    if (count > 1)
                        _stack.SetCount(item, originalCount * count, stack);
                }
            }

            // Required structure shipments bypass the player-facing Item/size checks, retaining capacity and insertion vetoes.
            if (!required && !_storage.CanInsert(item, crate, storage) ||
                !_storage.Insert(item, crate, storage, mergeStacks: false))
            {
                if (delivered == 0)
                {
                    if (stack != null && stack.Count != originalCount)
                        _stack.SetCount(item, originalCount, stack);

                    QueueDel(crate);
                    return false;
                }

                // This unit has not been delivered or counted; retry it on the next dispatch.
                QueueDel(item);
                break;
            }

            delivered += count;
        }

        package = crate;
        return delivered > 0;
    }
}
