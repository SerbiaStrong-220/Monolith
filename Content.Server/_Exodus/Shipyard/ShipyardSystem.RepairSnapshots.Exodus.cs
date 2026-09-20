using Content.Server.Shuttles.Components;
using Content.Shared._Exodus.Shipyard;
using Content.Shared._Mono.ShipRepair;
using Content.Shared._Mono.ShipRepair.Components;
using Content.Shared._Mono.Ships.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.BUI;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Robust.Shared.Map.Components;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private SharedShipRepairSystem _shipRepair = default!;

    private void InitializeRepairSnapshots()
    {
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardRepairSnapshotMessage>(OnRepairSnapshot);
    }

    private RepairSnapshotQuote? GetRepairSnapshotQuote(EntityUid? targetId)
    {
        if (!TryComp<ShuttleDeedComponent>(targetId, out var deed) ||
            deed.ShuttleUid is not { } grid || TerminatingOrDeleted(grid) ||
            !HasComp<MapGridComponent>(grid) || !HasComp<ShuttleComponent>(grid) ||
            !TryComp<ShuttleDeedComponent>(grid, out var registeredDeed) || registeredDeed.ShuttleUid != grid ||
            !TryComp<VesselComponent>(grid, out var vessel) ||
            !_prototypeManager.TryIndex(vessel.VesselId, out var prototype) || prototype.Price <= 0 ||
            !TryComp<ShipRepairDataComponent>(grid, out var repairData))
        {
            return null;
        }

        var snapshotPrice = Math.Max(1, prototype.Price / 2);
        return new RepairSnapshotQuote(GetNetEntity(grid), snapshotPrice, repairData.Revision);
    }

    private void OnRepairSnapshot(Entity<ShipyardConsoleComponent> ent, ref ShipyardRepairSnapshotMessage args)
    {
        var player = args.Actor;
        if (TerminatingOrDeleted(player) || TerminatingOrDeleted(ent) ||
            args.UiKey is not ShipyardConsoleUiKey uiKey || !_ui.IsUiOpen(ent.Owner, uiKey, player))
        {
            return;
        }

        var paid = false;
        var committed = false;
        try
        {
            if (!_enabled ||
                TryComp<AccessReaderComponent>(ent, out var access) && !_access.IsAllowed(player, ent, access))
            {
                RejectRepairSnapshot(ent, player, "shipyard-snapshot-access-denied");
                return;
            }

            var targetId = ent.Comp.TargetIdSlot.ContainerSlot?.ContainedEntity;
            if (GetRepairSnapshotQuote(targetId) is not { } quote)
            {
                RejectRepairSnapshot(ent, player, "shipyard-snapshot-no-ship");
                return;
            }

            // Also rejects duplicate requests once the first purchase advances the revision.
            if (quote != args.Quote)
            {
                RejectRepairSnapshot(ent, player, "shipyard-snapshot-quote-changed");
                return;
            }

            var grid = GetEntity(quote.Grid);
            if (!IsDockedAtSnapshotShipyard(ent.Owner, grid))
            {
                RejectRepairSnapshot(ent, player, "shipyard-snapshot-undocked");
                return;
            }

            if (!_bank.TryBankWithdraw(player, quote.Price, dry: true))
            {
                RejectRepairSnapshot(ent, player, "shipyard-snapshot-payment-failed");
                return;
            }

            // Build separately: a scan failure must leave the old snapshot and the balance intact.
            if (!_shipRepair.TryCreateRepairData(grid, out var snapshot) ||
                TerminatingOrDeleted(grid) || !TryComp<ShipRepairDataComponent>(grid, out var repairData) ||
                GetRepairSnapshotQuote(ent.Comp.TargetIdSlot.ContainerSlot?.ContainedEntity) != quote)
            {
                RejectRepairSnapshot(ent, player, "shipyard-snapshot-failed");
                return;
            }

            if (!_bank.TryBankWithdraw(player, quote.Price))
            {
                RejectRepairSnapshot(ent, player, "shipyard-snapshot-payment-failed");
                return;
            }

            paid = true;
            if (repairData.Revision != quote.Revision)
                throw new InvalidOperationException("The repair snapshot changed during payment.");

            _shipRepair.ApplyRepairData((grid, repairData), snapshot);
            committed = true;

            _popup.PopupEntity(Loc.GetString("shipyard-snapshot-success", ("cost", quote.Price)), player, player);
            PlayConfirmSound(player, ent.Owner, ent.Comp);
            _adminLogger.Add(LogType.ShipYardUsage, LogImpact.Medium,
                $"{ToPrettyString(player):actor} updated the SRD snapshot of {ToPrettyString(grid)} for {quote.Price} credits using {ToPrettyString(targetId)} at {ToPrettyString(ent.Owner)} (revision {repairData.Revision}).");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to purchase an SRD snapshot at {ToPrettyString(ent.Owner)}: {ex}");
            if (!committed)
            {
                if (paid && !_bank.TryBankDeposit(player, args.Quote.Price, tax: false))
                {
                    _adminLogger.Add(LogType.ShipYardUsage, LogImpact.High,
                        $"Failed to refund {args.Quote.Price} credits to {ToPrettyString(player):actor} after an SRD snapshot purchase failure.");
                }

                RejectRepairSnapshot(ent, player, "shipyard-snapshot-failed");
            }
        }
        finally
        {
            RefreshRepairSnapshotState(ent, player, uiKey);
        }
    }

    private bool IsDockedAtSnapshotShipyard(EntityUid console, EntityUid ship)
    {
        if (Transform(console).GridUid is not { } consoleGrid || consoleGrid == ship)
            return false;

        var station = _station.GetOwningStation(console);

        foreach (var dock in _docking.GetDocks(ship))
        {
            if (!TryComp<TransformComponent>(dock.Comp.DockedWith, out var otherDock) ||
                otherDock.GridUid is not { } dockedGrid || dockedGrid == ship)
            {
                continue;
            }

            if (dockedGrid == consoleGrid || station != null && _station.GetOwningStation(dockedGrid) == station)
                return true;
        }

        return false;
    }

    private void RejectRepairSnapshot(Entity<ShipyardConsoleComponent> console, EntityUid player, string message)
    {
        if (!TerminatingOrDeleted(player))
            _popup.PopupEntity(Loc.GetString(message), player, player);

        if (!TerminatingOrDeleted(console))
            PlayDenySound(player, console.Owner, console.Comp);
    }

    private void RefreshRepairSnapshotState(Entity<ShipyardConsoleComponent> console, EntityUid player, ShipyardConsoleUiKey uiKey)
    {
        if (TerminatingOrDeleted(console) || TerminatingOrDeleted(player) || !_ui.IsUiOpen(console.Owner, uiKey, player))
            return;

        var targetId = console.Comp.TargetIdSlot.ContainerSlot?.ContainedEntity;
        var title = TryComp<ShuttleDeedComponent>(targetId, out var deed) ? GetFullName(deed) : null;
        var balance = TryComp<BankAccountComponent>(player, out var bank) ? bank.Balance : 0;
        if (!_ui.TryGetUiState<ShipyardConsoleInterfaceState>(console.Owner, uiKey, out var state))
            return;

        RefreshState(console, balance, state.AccessGranted, title, state.ShipSellValue, targetId, uiKey, state.FreeListings);
    }
}
