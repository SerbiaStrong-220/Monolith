using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Exodus.SafetyDepositBox;
using Content.Shared.Hands;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Item;
using Content.Shared.Storage;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.SafetyDepositBox;

public sealed partial class SafetyDepositBoxSystem
{
    private bool TryGetAdminHandItem(ICommonSession admin, out AdminHandItem source)
    {
        source = default;
        if (!TryGetAdminRecipient(admin, out var holder) ||
            !_hands.TryGetActiveHand(holder, out var hand) || hand.HeldEntity is not { } item ||
            TerminatingOrDeleted(item) || !Initialized(item) ||
            !HasComp<ItemComponent>(item) || HasComp<VirtualItemComponent>(item) ||
            !_hands.CanDrop(holder, item))
            return false;

        source = new AdminHandItem(holder, item, hand.Name);
        return true;
    }

    private bool IsAdminHandItemValid(ICommonSession admin, AdminHandItem source)
    {
        return TryGetAdminHandItem(admin, out var current) && current == source;
    }

    private bool TryInsertAdminHandItem(Entity<StorageComponent> storage, AdminHandItem source)
    {
        if (TerminatingOrDeleted(storage) || TerminatingOrDeleted(source.Item) ||
            !_hands.CanDrop(source.Holder, source.Item) ||
            !_storage.CanInsert(storage, source.Item, out _, storage.Comp, ignoreStacks: true) ||
            !_storage.Insert(storage, source.Item, out _, storageComp: storage.Comp,
                playSound: false, stackAutomatically: false))
            return false;

        // Container insertion raises hand unequip events; deselection is normally raised by Hands.DoDrop.
        if (TerminatingOrDeleted(source.Item))
            return false;

        RaiseLocalEvent(source.Item, new HandDeselectedEvent(source.Holder));
        return !TerminatingOrDeleted(source.Item) && storage.Comp.Container.Contains(source.Item);
    }

    private async Task<string> AdminStoreHandItemAsync(
        ICommonSession admin,
        WayfarerSafetyDepositBox box,
        AdminSafetyDepositModifyMessage request,
        AdminHandItem source,
        AdminBoxStaging staging,
        Func<bool> canContinue)
    {
        if (!TryRestoreAdminBoxStorage(box, staging, out var temporaryBox))
            return "admin-safety-deposit-error-capacity";

        if (!canContinue() || !IsAdminHandItemValid(admin, source))
            return "admin-safety-deposit-error-hand";

        if (!_storage.CanInsert(temporaryBox, source.Item, out _, temporaryBox.Comp, ignoreStacks: true))
            return "admin-safety-deposit-error-capacity";

        var returnCoordinates = Transform(source.Holder).Coordinates;
        var pauses = new Dictionary<EntityUid, bool>();
        CollectAdminItemPauses(source.Item, pauses);
        var committed = false;
        var result = "admin-safety-deposit-error-capacity";
        try
        {
            if (TryInsertAdminHandItem(temporaryBox, source))
            {
                var itemData = SaveAdminItem(source.Item);
                var replacement = new List<string>(box.Items.Count + 1);
                foreach (var item in box.Items)
                    replacement.Add(item.EntityData);
                replacement.Add(itemData);

                var audit = CreateAdminAudit(admin, box, request.Action.ToString(), "success",
                    $"{DescribeAdminItem(source.Item)}; source: admin hand; destination: database; reason: {request.Reason.Trim()}", itemData);
                // Nullspace does not pause entities. Freeze the original and its children during the DB await.
                foreach (var entity in pauses.Keys)
                {
                    if (!TerminatingOrDeleted(entity))
                        SetPaused(entity, true);
                }

                if (canContinue())
                {
                    var outcome = await CommitAdminStoredChangeAsync(box, replacement, audit, staging);
                    committed = outcome == true;
                    result = outcome switch
                    {
                        true => "admin-safety-deposit-success",
                        false => "admin-safety-deposit-error-stale",
                        null => "admin-safety-deposit-error-recovery",
                    };

                    if (committed)
                        LogAdminBoxAction(admin, audit);
                }
                else
                {
                    result = "admin-safety-deposit-error-permission";
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Could not store held item {source.Item} in safety deposit box {box.BoxId}: {ex}");
            result = "admin-safety-deposit-error-database";
        }
        finally
        {
            // The original is never owned by generic staging cleanup until a commit is confirmed.
            // On an ambiguous commit it must remain inaccessible, because the DB may already contain it.
            if (!committed && !staging.Retain)
            {
                try
                {
                    if (!TryReturnAdminHandItem(source, returnCoordinates, pauses))
                        staging.Retain = true;
                }
                catch (Exception ex)
                {
                    staging.Retain = true;
                    Log.Error($"Could not return held safety deposit item {source.Item}: {ex}");
                }
            }

            if (committed || staging.Retain)
                staging.Entities.Add(source.Item);

            if (staging.Retain)
                result = "admin-safety-deposit-error-recovery";
        }

        return result;
    }

    private void CollectAdminItemPauses(EntityUid item, Dictionary<EntityUid, bool> pauses)
    {
        if (TerminatingOrDeleted(item))
            return;

        pauses.Add(item, Paused(item));
        var children = Transform(item).ChildEnumerator;
        while (children.MoveNext(out var child))
            CollectAdminItemPauses(child, pauses);
    }

    private bool TryReturnAdminHandItem(
        AdminHandItem source,
        EntityCoordinates returnCoordinates,
        Dictionary<EntityUid, bool> pauses)
    {
        if (TerminatingOrDeleted(source.Item))
            return false;

        var holderAvailable = !TerminatingOrDeleted(source.Holder) &&
                              Transform(source.Holder).MapID != MapId.Nullspace;
        if (holderAvailable)
            returnCoordinates = Transform(source.Holder).Coordinates;

        if (TerminatingOrDeleted(returnCoordinates.EntityId) ||
            Transform(returnCoordinates.EntityId).MapID == MapId.Nullspace)
            return false;

        if (!holderAvailable || !_hands.IsHolding(source.Holder, source.Item, out _))
        {
            if (_container.TryGetContainingContainer(source.Item, out var container) &&
                !_container.Remove(source.Item, container, destination: returnCoordinates))
                return false;

            _transform.SetCoordinates(source.Item, returnCoordinates);
            if (holderAvailable && !_hands.TryPickup(source.Holder, source.Item, source.HandName,
                    checkActionBlocker: false, animate: false))
                _hands.TryPickupAnyHand(source.Holder, source.Item);
        }

        foreach (var (entity, paused) in pauses)
        {
            if (TerminatingOrDeleted(entity))
                continue;

            var map = Transform(entity).MapUid;
            SetPaused(entity, paused || (map is { } mapUid && Paused(mapUid)));
        }

        return !TerminatingOrDeleted(source.Item) && Transform(source.Item).MapID != MapId.Nullspace;
    }

    private readonly record struct AdminHandItem(EntityUid Holder, EntityUid Item, string HandName);
}
