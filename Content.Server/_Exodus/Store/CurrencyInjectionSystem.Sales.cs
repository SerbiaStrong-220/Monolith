using Content.Server._Mono.Store.Components;
using Content.Server.Cargo.Systems;
using Content.Shared._Mono.Company;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Store;

public sealed partial class CurrencyInjectionSystem
{
    private void OnEntitiesSold(ref EntitySoldEvent args)
    {
        Dictionary<ProtoId<CompanyPrototype>, Dictionary<string, FixedPoint2>>? totals = null;
        foreach (var uid in args.Sold)
        {
            if (!TryComp<CurrencyInjectionOnSellComponent>(uid, out var injection))
                continue;

            totals ??= new();
            if (!totals.TryGetValue(injection.Company, out var currencies))
            {
                currencies = new();
                totals.Add(injection.Company, currencies);
            }

            foreach (var (currency, amount) in injection.Amount)
                currencies[currency] = currencies.GetValueOrDefault(currency) + amount;
        }

        if (totals == null)
            return;

        foreach (var (company, currencies) in totals)
            InjectCurrency(company, currencies);
    }
}
