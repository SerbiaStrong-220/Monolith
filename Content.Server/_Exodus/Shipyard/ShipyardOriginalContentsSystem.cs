// (c) Space Exodus Team - EXDS-RL with CLA
using Content.Server._Exodus.Economy;
using Content.Server._NF.Shipyard;
using Content.Server.Shuttles.Components;
using Content.Shared._Mono.Shipyard;

namespace Content.Server._Exodus.Shipyard;

/// <summary>
/// Captures the type and quantity of equipment supplied with a ship. The final purchase checkpoint
/// includes purchase modifiers; later repairs, replacement entities and cargo never redefine it.
/// </summary>
public sealed partial class ShipyardOriginalContentsSystem : EntitySystem
{
    [Dependency] private MarketStockIntakeSystem _intake = default!;

    public override void Initialize()
    {
        base.Initialize();
        // ShuttleComponent's subscription is already owned by the repair system.
        SubscribeLocalEvent<TransformComponent, ShipBoughtEvent>(OnShipBought);
        SubscribeLocalEvent<ShipyardShuttlePurchaseEvent>(OnPurchaseCompleted);
    }

    private void OnShipBought(Entity<TransformComponent> ent, ref ShipBoughtEvent args)
    {
        if (TryComp<ShuttleComponent>(ent, out var shuttle))
            CaptureShip((ent.Owner, shuttle), false);
    }

    private void OnPurchaseCompleted(ShipyardShuttlePurchaseEvent args)
    {
        if (!TerminatingOrDeleted(args.Shuttle) && TryComp<ShuttleComponent>(args.Shuttle, out var shuttle))
            CaptureShip((args.Shuttle, shuttle), true);
    }

    private void CaptureShip(Entity<ShuttleComponent> ship, bool finalized)
    {
        if (TerminatingOrDeleted(ship))
            return;

        var baseline = EnsureComp<ShipyardOriginalContentsComponent>(ship);
        if (baseline.Finalized || baseline.Loaded && !finalized)
            return;

        _intake.CaptureOriginalContents((ship.Owner, baseline));
        baseline.Loaded = true;
        baseline.Finalized = finalized;
    }
}
