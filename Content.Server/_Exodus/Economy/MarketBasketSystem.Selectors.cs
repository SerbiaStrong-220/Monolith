// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Shared.EntityTable;
using Content.Shared.EntityTable.EntitySelectors;
using Content.Shared.EntityTable.ValueSelector;

namespace Content.Server._Exodus.Economy;

public sealed partial class MarketBasketSystem
{
    private bool AddSelector(EntityTableSelector selector, double count, BuildState state, int depth)
    {
        if (!Visit(state, depth) || !double.IsFinite(selector.Prob) || selector.Prob < 0)
            return state.Fail("Invalid or excessive entity table in market basket.");

        if (selector.Prob == 0)
            return true;

        if (selector.Prob < 1)
            state.Exact = false;

        if (!TryGetUpperCount(selector.Rolls, state, out var rolls))
            return false;

        count *= rolls;
        if (count == 0)
            return true;
        if (!double.IsFinite(count) || count < 0 || count > MaxQuantity)
            return state.Fail("Excessive entity table quantity in market basket.");

        switch (selector)
        {
            case NoneSelector:
                return true;
            case EntSelector entity:
                return TryGetUpperCount(entity.Amount, state, out var amount) &&
                       AddPrototype(entity.Id, count * amount, state, depth + 1);
            case AllSelector all:
                foreach (var child in all.Children)
                {
                    if (!AddSelector(child, count, state, depth + 1))
                        return false;
                }
                return true;
            case GroupSelector group:
                var choices = 0;
                foreach (var child in group.Children)
                {
                    if (!float.IsFinite(child.Weight) || child.Weight < 0)
                        return state.Fail("Invalid entity table weight in market basket.");
                    if (child.Weight == 0)
                        continue;
                    choices++;
                    if (!AddSelector(child, count, state, depth + 1))
                        return false;
                }
                if (choices != 1)
                    state.Exact = false;
                return true;
            case NestedSelector nested:
                if (!_prototypes.TryIndex<EntityTablePrototype>(nested.TableId, out var table) ||
                    !state.Tables.Add(nested.TableId.Id))
                {
                    return state.Fail($"Unknown or cyclic entity table {nested.TableId}.");
                }
                try
                {
                    return AddSelector(table.Table, count, state, depth + 1);
                }
                finally
                {
                    state.Tables.Remove(nested.TableId.Id);
                }
            default:
                return state.Fail($"Unsupported entity selector {selector.GetType().Name}.");
        }
    }

    private static bool TryGetUpperCount(NumberSelector selector, BuildState state, out double count)
    {
        switch (selector)
        {
            case ConstantNumberSelector constant:
                count = Math.Floor(constant.Value);
                break;
            case RangeNumberSelector range:
                if (!float.IsFinite(range.Range.X) || range.Range.X < 0 || range.Range.Y < range.Range.X)
                {
                    count = 0;
                    return state.Fail("Invalid entity table range in market basket.");
                }
                // RangeNumberSelector draws below Y + 1 before flooring, including fractional endpoints.
                count = Math.Ceiling(range.Range.Y);
                if (Math.Floor(range.Range.X) != count)
                    state.Exact = false;
                break;
            default:
                count = 0;
                return state.Fail($"Unsupported number selector {selector.GetType().Name}.");
        }

        return double.IsFinite(count) && count >= 0 && count <= MaxQuantity ||
               state.Fail("Invalid entity table count in market basket.");
    }
}
