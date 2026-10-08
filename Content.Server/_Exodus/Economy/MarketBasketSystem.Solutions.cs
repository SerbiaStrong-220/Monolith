// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server.Chemistry.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Random;
using Content.Shared.Stacks;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketBasketSystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;

    /// <summary>
    /// Reads actual reagents independently of opaque price overrides. Predicates prune whole subtrees.
    /// Internal solution entities are counted through their owner exactly once.
    /// </summary>
    public bool TryGetReagentContents(EntityUid uid, out MarketBasket basket, out string? failure,
        Func<EntityUid, bool>? predicate = null)
    {
        var state = new BuildState();
        if (!AddReagentContents(uid, state, 0, predicate))
        {
            basket = EmptyBasket;
            failure = state.Failure;
            return false;
        }

        basket = new MarketBasket(state.Lines, 1, true);
        failure = null;
        return true;
    }

    private bool AddReagentContents(EntityUid uid, BuildState state, int depth, Func<EntityUid, bool>? predicate)
    {
        if (!Visit(state, depth) || Deleted(uid) || !state.Entities.Add(uid))
            return state.Fail("Invalid or cyclic reagent container.");
        if (predicate != null && !predicate(uid))
            return true;

        if (HasComp<SolutionContainerManagerComponent>(uid))
        {
            if (!AddEntitySolutions(uid, Prototype(uid)?.ID, state))
                return false;
        }

        if (!TryComp<ContainerManagerComponent>(uid, out var containers))
            return true;

        foreach (var container in containers.Containers.Values)
        {
            foreach (var child in container.ContainedEntities)
            {
                if (!HasComp<SolutionComponent>(child) && !AddReagentContents(child, state, depth + 1, predicate))
                    return false;
            }
        }
        return true;
    }

    private bool AddEntitySolutions(EntityUid uid, EntProtoId? prototype, BuildState state)
    {
        if (!TryComp<SolutionContainerManagerComponent>(uid, out var manager))
            return true;

        if (MetaData(uid).EntityLifeStage < EntityLifeStage.MapInitialized)
        {
            foreach (var (_, solution) in _solutions.EnumerateSolutions(manager))
            {
                if (!AddSolution(solution, prototype, 1, state, uid))
                    return false;
            }
            return true;
        }

        foreach (var (_, solution) in _solutions.EnumerateSolutions((uid, manager)))
        {
            if (!AddSolution(solution.Comp.Solution, prototype, 1, state, uid))
                return false;
        }
        return true;
    }

    private bool AddSolution(Solution solution, EntProtoId? prototype, double count, BuildState state,
        EntityUid? sourceEntity = null)
    {
        foreach (var (reagent, quantity) in solution.Contents)
        {
            if (!AddReagent(prototype, reagent.Prototype, quantity.Double() * count, state, sourceEntity))
                return false;
        }
        return true;
    }

    private bool AddReagent(EntProtoId? prototype, ProtoId<ReagentPrototype> reagent, double quantity,
        BuildState state, EntityUid? sourceEntity = null)
    {
        if (!Visit(state, 0) || !_prototypes.TryIndex(reagent, out var data))
            return state.Fail($"Unknown reagent {reagent} in {prototype}.");
        if (!double.IsFinite(quantity) || quantity < 0 || quantity > MaxQuantity ||
            !double.IsFinite(data.PricePerUnit) || data.PricePerUnit < 0)
            return state.Fail($"Invalid reagent appraisal for {reagent} in {prototype}.");
        if (data.PricePerUnit == 0 || quantity == 0)
            return true;
        if (prototype is not { } ownerPrototype)
            return state.Fail("Cannot appraise priced reagents in an unprototyped container.");

        // Reagents can change carriers. Neither item tax nor carrier exemptions follow the liquid.
        return AddLine(state, new MarketBasketLine(ownerPrototype, DynamicMarketSystem.ReagentKey(reagent),
            data.PricePerUnit, quantity, SourceEntity: sourceEntity));
    }

    private bool AddPrototypeSolutions(EntityPrototype prototype, double count, BuildState state)
    {
        var amounts = new Dictionary<ProtoId<ReagentPrototype>, double>();
        if (!CollectPrototypeReagents(prototype, amounts, state))
            return false;

        if (prototype.TryGetComponent<StackComponent>(out var stack, _factory))
        {
            if (!_prototypes.TryIndex<StackPrototype>(stack.StackTypeId, out var stackType) ||
                !_prototypes.TryIndex(stackType.Spawn, out var splitPrototype))
                return state.Fail($"Invalid reagent stack {prototype.ID}.");

            var splitAmounts = new Dictionary<ProtoId<ReagentPrototype>, double>();
            if (!CollectPrototypeReagents(splitPrototype, splitAmounts, state))
                return false;

            // Splitting currently recreates canonical solutions. Bound all single-unit extracts,
            // including variants with a different initial mixture, without inventing delivered demand.
            foreach (var (reagent, quantity) in splitAmounts)
            {
                var original = amounts.GetValueOrDefault(reagent);
                if (quantity > original)
                {
                    amounts[reagent] = quantity;
                    state.Exact = false;
                }
            }
            if (amounts.Count > 0 && stack.Count > 1)
            {
                count *= stack.Count;
                state.Exact = false;
            }
        }

        foreach (var (reagent, quantity) in amounts)
        {
            if (!AddReagent(prototype.ID, reagent, quantity * count, state))
                return false;
        }
        return true;
    }

    private bool CollectPrototypeReagents(EntityPrototype prototype,
        Dictionary<ProtoId<ReagentPrototype>, double> amounts, BuildState state)
    {
        if (prototype.TryGetComponent<SolutionContainerManagerComponent>(out var manager, _factory))
        {
            foreach (var (_, solution) in _solutions.EnumerateSolutions(manager))
            {
                foreach (var (reagent, quantity) in solution.Contents)
                {
                    if (!Visit(state, 0) || quantity < 0 ||
                        !_prototypes.TryIndex<ReagentPrototype>(reagent.Prototype, out var data) ||
                        !double.IsFinite(data.PricePerUnit) || data.PricePerUnit < 0)
                        return state.Fail($"Invalid reagent quantity in {prototype.ID}.");
                    if (data.PricePerUnit == 0 || quantity == 0)
                        continue;
                    amounts[reagent.Prototype] = amounts.GetValueOrDefault(reagent.Prototype) + quantity.Double();
                }
            }
        }

        if (!prototype.TryGetComponent<RandomFillSolutionComponent>(out var random, _factory) ||
            random.WeightedRandomId is not { } fillId)
            return true;
        if (!_prototypes.TryIndex<WeightedRandomFillSolutionPrototype>(fillId, out var fills))
            return state.Fail($"Unknown random solution fill {fillId}.");

        state.Exact = false;
        var maximums = new Dictionary<ProtoId<ReagentPrototype>, double>();
        foreach (var fill in fills.Fills)
        {
            if (!Visit(state, 0) || !float.IsFinite(fill.Weight) || fill.Weight < 0 || fill.Quantity < 0)
                return state.Fail($"Invalid random solution fill {fillId}.");
            if (fill.Weight == 0)
                continue;
            foreach (var reagent in fill.Reagents)
            {
                if (!Visit(state, 0) || !_prototypes.TryIndex<ReagentPrototype>(reagent, out var data) ||
                    !double.IsFinite(data.PricePerUnit) || data.PricePerUnit < 0)
                    return state.Fail($"Invalid reagent {reagent} in random fill {fillId}.");
                if (data.PricePerUnit == 0 || fill.Quantity == 0)
                    continue;
                maximums[reagent] = Math.Max(maximums.GetValueOrDefault(reagent), fill.Quantity.Double());
            }
        }
        foreach (var (reagent, quantity) in maximums)
            amounts[reagent] = amounts.GetValueOrDefault(reagent) + quantity;
        return true;
    }
}
