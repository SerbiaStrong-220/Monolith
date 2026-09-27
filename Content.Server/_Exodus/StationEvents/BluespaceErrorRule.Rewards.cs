using Content.Server._Mono.Store.Components;
using Content.Server._NF.StationEvents.Components;

namespace Content.Server._NF.StationEvents.Events;

public sealed partial class BluespaceErrorRule
{
    private void RewardPreservedBluespaceGrid(Entity<BluespaceErrorRuleComponent> rule, EntityUid grid, double value)
    {
        if (rule.Comp.StartingValue <= 0
            || !TryComp<CurrencyInjectionOnBluespaceErrorComponent>(rule, out var injection)
            || value / rule.Comp.StartingValue <= injection.IntegrityRequirement)
        {
            return;
        }

        var metadataQuery = GetEntityQuery<MetaDataComponent>();
        foreach (var required in injection.RequiredEntities)
        {
            var found = false;
            var children = Transform(grid).ChildEnumerator;
            while (children.MoveNext(out var child))
            {
                if (!metadataQuery.TryGetComponent(child, out var metadata)
                    || metadata.EntityLifeStage >= EntityLifeStage.Terminating
                    || metadata.EntityPrototype?.ID != required.Id)
                {
                    continue;
                }

                found = true;
                break;
            }

            if (!found)
                return;
        }

        _currencyInjection.InjectCurrency(injection.Company, injection.Amount);
    }
}
