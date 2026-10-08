// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server.Chemistry.Components;
using Content.Server.Construction.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Dispenser;
using Content.Shared.Construction.Components;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Containers;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Stacks;
using Content.Shared.Storage.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketBasketSystem
{
    /// <summary>
    /// Resolves the pack slots recreated by a fresh dispenser, independently of its live upgrades.
    /// Missing dispensers or packs have no generated contents; unsupported initial state fails closed.
    /// </summary>
    public bool TryGetReagentDispenserFillCount(EntityPrototype prototype, out int count)
    {
        count = 0;
        if (!prototype.TryGetComponent<ReagentDispenserComponent>(out var dispenser, _factory) ||
            dispenser.PackPrototypeId == null)
        {
            return true;
        }

        if (!_prototypes.TryIndex<ReagentDispenserInventoryPrototype>(dispenser.PackPrototypeId, out var pack) ||
            dispenser.StorageSlots.Count > 0 || dispenser.StorageSlotIds.Count > 0)
        {
            return false;
        }

        // Only Machine's initial RefreshParts creates the dispenser's storage slots.
        if (!prototype.TryGetComponent<MachineComponent>(out var machine, _factory))
            return true;

        // Competing fills can replace the board or upgrade parts before RefreshParts.
        if (prototype.TryGetComponent<ContainerFillComponent>(out var fill, _factory) &&
            (fill.Containers.ContainsKey(MachineFrameComponent.BoardContainerName) ||
             fill.Containers.ContainsKey(MachineFrameComponent.PartContainerName)))
        {
            return false;
        }

        if (prototype.TryGetComponent<EntityTableContainerFillComponent>(out var tableFill, _factory))
        {
            var tableContainers = tableFill.Containers;
            if (tableContainers.ContainsKey(MachineFrameComponent.BoardContainerName) ||
                tableContainers.ContainsKey(MachineFrameComponent.PartContainerName))
                return false;
        }

        float partCount = 0;
        float totalRating = 0;
        if (machine.Board is { } boardId)
        {
            if (!_prototypes.TryIndex(boardId, out var boardPrototype) ||
                !boardPrototype.TryGetComponent<MachineBoardComponent>(out var board, _factory))
            {
                return false;
            }

            foreach (var (typeId, amount) in board.Requirements)
            {
                if (!machine.PartOverrides.TryGetValue(typeId, out var partId))
                {
                    if (!_prototypes.TryIndex(typeId, out var type))
                        return false;
                    partId = type.StockPartPrototype;
                }
                if (!AddDispenserPartRating(partId, amount, false, dispenser.SlotUpgradeMachinePart,
                        ref partCount, ref totalRating))
                {
                    return false;
                }
            }

            foreach (var (stackId, amount) in board.StackRequirements)
            {
                if (!_prototypes.TryIndex(stackId, out var stack) ||
                    !AddDispenserPartRating(stack.Spawn, amount, true, dispenser.SlotUpgradeMachinePart,
                        ref partCount, ref totalRating))
                {
                    return false;
                }
            }

            foreach (var info in board.ComponentRequirements.Values)
            {
                if (!AddDispenserPartRating(info.DefaultPrototype, info.Amount, false, dispenser.SlotUpgradeMachinePart,
                        ref partCount, ref totalRating))
                {
                    return false;
                }
            }

            foreach (var info in board.TagRequirements.Values)
            {
                if (!AddDispenserPartRating(info.DefaultPrototype, info.Amount, false, dispenser.SlotUpgradeMachinePart,
                        ref partCount, ref totalRating))
                {
                    return false;
                }
            }
        }

        var rating = partCount > 0 ? totalRating / partCount : 1f;
        var extraSlots = dispenser.ExtraSlotsPerTier * (rating - 1f);
        if (!float.IsFinite(extraSlots) || extraSlots < int.MinValue || extraSlots >= int.MaxValue)
            return false;

        var capacity = (long)dispenser.BaseNumStorageSlots + (int)extraSlots;
        if (capacity < 0 || capacity > MaxVisitedNodes)
            return false;

        count = Math.Min((int)capacity, pack.Inventory.Count);
        return true;
    }

    private bool AddDispenserPartRating(EntProtoId id, int count, bool stackUnits,
        ProtoId<MachinePartPrototype> partType, ref float partCount, ref float totalRating)
    {
        if (count == 0)
            return true;
        if (count < 0 || !_prototypes.TryIndex(id, out var prototype))
            return false;
        if (!prototype.TryGetComponent<MachinePartComponent>(out var part, _factory) || part.PartType != partType)
            return true;

        float units = count;
        if (!stackUnits && prototype.TryGetComponent<StackComponent>(out var stack, _factory))
            units *= stack.Count;
        if (units < 0 || part.Rating < 0)
            return false;

        partCount += units;
        totalRating += units * part.Rating;
        return float.IsFinite(partCount) && float.IsFinite(totalRating);
    }

    private bool AddReagentDispenserContents(EntityPrototype prototype, double count, BuildState state, int depth)
    {
        if (!prototype.TryGetComponent<ReagentDispenserComponent>(out var dispenser, _factory))
            return true;
        if (!TryGetReagentDispenserFillCount(prototype, out var fillCount))
            return state.Fail($"Unsupported initial dispenser contents on {prototype.ID}.");

        prototype.TryGetComponent<ItemSlotsComponent>(out var slots, _factory);
        var initialSlots = slots?.Slots;
        if (initialSlots != null && initialSlots.ContainsKey(SharedReagentDispenser.OutputSlotName))
            return state.Fail($"Conflicting dispenser output slot on {prototype.ID}.");
        if (!string.IsNullOrEmpty(dispenser.BeakerSlot.StartingItem) &&
            !AddPrototype(dispenser.BeakerSlot.StartingItem, count, state, depth))
            return false;

        if (fillCount == 0)
            return true;
        if (dispenser.PackPrototypeId is not { } packId ||
            !_prototypes.TryIndex<ReagentDispenserInventoryPrototype>(packId, out var pack))
            return state.Fail($"Unknown dispenser inventory on {prototype.ID}.");

        for (var i = 0; i < fillCount; i++)
        {
            if (initialSlots != null && initialSlots.ContainsKey(ReagentDispenserComponent.BaseStorageSlotId + i))
                return state.Fail($"Conflicting dispenser storage slot on {prototype.ID}.");
            if (!AddPrototype(pack.Inventory[i], count, state, depth))
                return false;
        }

        return true;
    }
}
