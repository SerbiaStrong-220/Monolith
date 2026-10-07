// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Body;
using Content.Server._EstacaoPirata.OpenTriggeredStorageFill;
using Content.Server.Construction.Components;
using Content.Shared._Shitmed.Autodoc.Components;
using Content.Shared.Body.Components;
using Content.Shared.Clothing.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Containers;
using Content.Shared.Hands.Components;
using Content.Shared.Stacks;
using Content.Shared.Storage.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketBasketSystem
{
    private bool AddGeneratedContents(EntityPrototype prototype, double count, BuildState state, int depth)
    {
        if (prototype.TryGetComponent<MachineComponent>(out var machine, _factory) &&
            !AddMachineContents(prototype, machine, count, state, depth + 1))
        {
            return false;
        }

        if (prototype.TryGetComponent<ComputerComponent>(out var computer, _factory) &&
            !string.IsNullOrEmpty(computer.BoardPrototype))
        {
            if (prototype.TryGetComponent<ContainerFillComponent>(out var fill, _factory) &&
                fill.Containers.ContainsKey("board"))
            {
                // Existing board contents can suppress CreateComputerBoard depending on MapInit order.
                state.Exact = false;
            }

            if (!AddPrototype(computer.BoardPrototype, count, state, depth + 1))
                return false;
        }

        if (prototype.TryGetComponent<BodyComponent>(out var body, _factory) &&
            !AddBodyContents(prototype, body, count, state, depth + 1))
        {
            return false;
        }

        if (prototype.TryGetComponent<BinComponent>(out var bin, _factory))
        {
            if (bin.InitialContents.Count > bin.MaxItems || bin.Whitelist != null)
                state.Exact = false;

            foreach (var child in bin.InitialContents)
            {
                if (!AddPrototype(child, count, state, depth + 1))
                    return false;
            }
        }

        if (prototype.TryGetComponent<HandsFillComponent>(out var hands, _factory) &&
            prototype.TryGetComponent<HandsComponent>(out _, _factory))
        {
            foreach (var child in hands.Hands.Values)
            {
                if (child is { } id && !AddPrototype(id, count, state, depth + 1))
                    return false;
            }
        }

        if (prototype.TryGetComponent<OpenTriggeredStorageFillComponent>(out var delayed, _factory) &&
            !AddEntries(delayed.Contents, count, state, depth + 1))
        {
            return false;
        }

        if (prototype.TryGetComponent<LoadoutComponent>(out var loadout, _factory))
        {
            if (loadout.RoleLoadout is { Count: > 0 } || loadout.Components.Count > 0)
                return state.Fail($"Role or component loadout on {prototype.ID} requires a basket adapter.");

            if (loadout.StartingGear is { } gears)
            {
                if (gears.Count > 1)
                    state.Exact = false;

                foreach (var id in gears)
                {
                    if (!_prototypes.TryIndex(id, out var gear))
                        return state.Fail($"Unknown starting gear {id}.");

                    foreach (var item in gear.Equipment.Values)
                    {
                        if (!AddPrototype(item, count, state, depth + 1))
                            return false;
                    }
                    foreach (var item in gear.Inhand)
                    {
                        if (!AddPrototype(item, count, state, depth + 1))
                            return false;
                    }
                    foreach (var contents in gear.Storage.Values)
                    {
                        foreach (var item in contents)
                        {
                            if (!AddPrototype(item, count, state, depth + 1))
                                return false;
                        }
                    }
                }
            }
        }

        return true;
    }

    private bool AddMachineContents(EntityPrototype prototype, MachineComponent machine, double count,
        BuildState state, int depth)
    {
        if (machine.Board is not { } boardId)
            return true;

        if (!_prototypes.TryIndex(boardId, out var boardPrototype) ||
            !boardPrototype.TryGetComponent<MachineBoardComponent>(out var board, _factory))
        {
            return state.Fail($"Unknown machine board {boardId}.");
        }

        if (prototype.TryGetComponent<ContainerFillComponent>(out var fill, _factory) &&
            fill.Containers.ContainsKey(MachineFrameComponent.BoardContainerName))
        {
            // Existing board contents suppress CreateBoardAndStockParts depending on MapInit order.
            state.Exact = false;
        }

        if (!AddPrototype(boardPrototype.ID, count, state, depth))
            return false;

        foreach (var (id, amount) in board.StackRequirements)
        {
            if (!_prototypes.TryIndex(id, out var stackType) ||
                !_prototypes.TryIndex(stackType.Spawn, out var stackPrototype) ||
                !stackPrototype.TryGetComponent<StackComponent>(out var stack, _factory) || stack.Count <= 0)
            {
                return state.Fail($"Invalid machine material requirement {id}.");
            }

            // SpawnMultiple receives stack units, whereas AddPrototype counts spawned entities.
            if (!AddPrototype(stackPrototype.ID, count * amount / stack.Count, state, depth))
                return false;
        }

        foreach (var info in board.ComponentRequirements.Values)
        {
            if (!AddPrototype(info.DefaultPrototype, count * info.Amount, state, depth))
                return false;
        }
        foreach (var info in board.TagRequirements.Values)
        {
            if (!AddPrototype(info.DefaultPrototype, count * info.Amount, state, depth))
                return false;
        }
        foreach (var (id, amount) in board.Requirements)
        {
            if (!machine.PartOverrides.TryGetValue(id, out var part))
            {
                if (!_prototypes.TryIndex(id, out var type))
                    return state.Fail($"Unknown machine part {id}.");
                part = type.StockPartPrototype;
            }
            if (!AddPrototype(part, count * amount, state, depth))
                return false;
        }

        return true;
    }

    private bool AddBodyContents(EntityPrototype prototype, BodyComponent body, double count,
        BuildState state, int depth)
    {
        if (body.Prototype is not { } id)
            return true;

        if (!_prototypes.TryIndex(id, out var anatomy) || !anatomy.Slots.TryGetValue(anatomy.Root, out var root))
            return state.Fail($"Invalid body prototype {id}.");

        if (root.Part == null)
            return true;

        prototype.TryGetComponent<StartingOrgansComponent>(out var replacements, _factory);
        var replaced = new HashSet<string>();
        var visited = new HashSet<string> { anatomy.Root };
        var pending = new Queue<string>();
        pending.Enqueue(anatomy.Root);
        while (pending.TryDequeue(out var slotId))
        {
            if (!Visit(state, depth) || !anatomy.Slots.TryGetValue(slotId, out var slot) || slot.Part is not { } part)
                return state.Fail($"Invalid body slot {slotId} in {id}.");

            if (!AddPrototype(part, count, state, depth + 1))
                return false;

            foreach (var (organSlot, organ) in slot.Organs)
            {
                EntProtoId organId = organ;
                if (replacements != null && replacements.Organs.TryGetValue(organSlot, out var replacement) &&
                    replaced.Add(organSlot))
                {
                    organId = replacement;
                }
                if (!AddPrototype(organId, count, state, depth + 1))
                    return false;
            }

            foreach (var connection in slot.Connections)
            {
                if (visited.Add(connection))
                    pending.Enqueue(connection);
            }
        }

        // Additional vacant organ slots are runtime state: include their possible replacements conservatively.
        if (replacements != null)
        {
            foreach (var (slot, organ) in replacements.Organs)
            {
                if (replaced.Contains(slot))
                    continue;
                state.Exact = false;
                if (!AddPrototype(organ, count, state, depth + 1))
                    return false;
            }
        }

        return true;
    }
}
