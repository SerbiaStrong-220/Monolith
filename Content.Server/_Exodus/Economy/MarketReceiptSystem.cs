// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server.Cargo.Systems;

namespace Content.Server._Exodus.Economy;

public sealed class MarketReceiptSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MarketReceiptComponent, PriceCalculationEvent>(OnPrice);
    }

    private void OnPrice(Entity<MarketReceiptComponent> ent, ref PriceCalculationEvent args)
    {
        args.Price = 0;
        args.Handled = true;
    }
}
