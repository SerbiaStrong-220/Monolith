using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Database._Exodus.SafetyDepositBox;
using Content.Shared._Exodus.SafetyDepositBox;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.SafetyDepositBox;

public sealed partial class SafetyDepositBoxSystem
{
    private async Task<string> AdminModifyStoredItemAsync(
        ICommonSession admin,
        WayfarerSafetyDepositBox box,
        AdminSafetyDepositModifyMessage request,
        AdminBoxStaging staging,
        Func<bool> canContinue)
    {
        EntityUid? itemEntity = null;
        string itemData;
        var replacement = new List<string>(box.Items.Count + 1);
        var description = string.Empty;

        if (request.Action == AdminSafetyDepositAction.Add)
        {
            if (box.LastWithdrawn != null || request.RecordId != 0 || request.Entity != null)
                return "admin-safety-deposit-error-invalid";

            itemEntity = SpawnAdminItem(request.PrototypeId.Trim(), staging);
            if (itemEntity is not { } added)
                return "admin-safety-deposit-error-invalid";

            if (!TryValidateStoredAddition(box, added, staging))
                return "admin-safety-deposit-error-capacity";

            itemData = SaveAdminItem(added);
            description = DescribeAdminItem(added);
            foreach (var item in box.Items)
                replacement.Add(item.EntityData);
            replacement.Add(itemData);
        }
        else
        {
            WayfarerSafetyDepositBoxItem? selected = null;
            foreach (var item in box.Items)
            {
                if (item.Id == request.RecordId)
                    selected = item;
                else
                    replacement.Add(item.EntityData);
            }

            if (selected == null || request.Entity != null)
                return "admin-safety-deposit-error-stale";

            itemData = selected.EntityData;
            description = $"stored item record {selected.Id}";
            if (request.Action == AdminSafetyDepositAction.Withdraw)
            {
                try
                {
                    itemEntity = LoadAdminItem(itemData, staging);
                    description += $", {DescribeAdminItem(itemEntity.Value)}";
                }
                catch (Exception ex)
                {
                    Log.Error($"Cannot restore safety deposit record {selected.Id}: {ex}");
                    return "admin-safety-deposit-error-restore";
                }
            }
            else if (Content.Server._Exodus.SafetyDepositBox.SafetyDepositItemMetadata.TryRead(
                         itemData, out var prototype, out var name, out var count))
            {
                description += $", prototype {prototype}, name {name}, saved count {count}";
            }
        }

        if (!canContinue())
            return "admin-safety-deposit-error-permission";

        var details = $"{description}; source: database; reason: {request.Reason.Trim()}";
        if (request.Action == AdminSafetyDepositAction.Withdraw && itemEntity is { } withdrawn)
        {
            return await AdminWithdrawStoredItemAsync(admin, box, replacement, withdrawn,
                itemData, details, staging, canContinue);
        }

        var audit = CreateAdminAudit(admin, box, request.Action.ToString(), "success", details, itemData);
        var committed = await CommitAdminStoredChangeAsync(box, replacement, audit, staging);
        if (committed == null)
            return "admin-safety-deposit-error-recovery";
        if (!committed.Value)
            return "admin-safety-deposit-error-stale";

        LogAdminBoxAction(admin, audit);
        return "admin-safety-deposit-success";
    }

    private bool TryValidateStoredAddition(WayfarerSafetyDepositBox box, EntityUid added, AdminBoxStaging staging)
    {
        return TryRestoreAdminBoxStorage(box, staging, out var temporaryBox) &&
               _storage.Insert(temporaryBox, added, out _, storageComp: temporaryBox.Comp,
                   playSound: false, stackAutomatically: false);
    }

    private bool TryRestoreAdminBoxStorage(
        WayfarerSafetyDepositBox box,
        AdminBoxStaging staging,
        out Entity<StorageComponent> temporaryStorage)
    {
        temporaryStorage = default;
        if (!TryGetBoxPrototype(box.ProtoId, out var prototype))
            return false;

        var temporaryBox = Spawn(prototype.ID, MapCoordinates.Nullspace);
        staging.Entities.Add(temporaryBox);
        if (!TryComp<StorageComponent>(temporaryBox, out var storage))
            return false;

        try
        {
            foreach (var record in box.Items)
            {
                var existing = LoadAdminItem(record.EntityData, staging);
                if (!_storage.Insert(temporaryBox, existing, out _, storageComp: storage,
                        playSound: false, stackAutomatically: false))
                    return false;
            }

            temporaryStorage = (temporaryBox, storage);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not validate safety deposit box {box.BoxId} capacity: {ex}");
            return false;
        }
    }

    private async Task<bool?> CommitAdminStoredChangeAsync(
        WayfarerSafetyDepositBox expected,
        List<string> replacement,
        SafetyDepositAdminAudit audit,
        AdminBoxStaging staging)
    {
        try
        {
            return await _dbManager.TryAdminReplaceSafetyDepositBoxItems(expected, replacement, audit);
        }
        catch (Exception ex)
        {
            Log.Error($"Safety deposit commit {audit.Id} failed; checking its durable receipt: {ex}");
            try
            {
                if (await _dbManager.GetSafetyDepositAdminAudit(audit.Id) != null)
                    return true;

                // An absent receipt is not proof that an interrupted commit cannot still complete.
                staging.Retain = true;
                Log.Error($"Safety deposit commit {audit.Id} has no confirmed receipt; box {audit.BoxId} stays locked.");
                return null;
            }
            catch (Exception receiptError)
            {
                staging.Retain = true;
                Log.Error($"Safety deposit commit {audit.Id} is ambiguous; box {audit.BoxId} stays locked: {receiptError}");
                return null;
            }
        }
    }

    private string DescribeAdminItem(EntityUid item)
    {
        var count = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
        return $"{ToPrettyString(item)}, prototype {Prototype(item)?.ID}, count {count}";
    }
}
