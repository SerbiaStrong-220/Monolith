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

        var audit = CreateAdminAudit(admin, box, request.Action.ToString(),
            request.Action == AdminSafetyDepositAction.Withdraw ? "pending" : "success",
            $"{description}; source: database; reason: {request.Reason.Trim()}", itemData);
        var committed = await CommitAdminStoredChangeAsync(box, replacement, audit, staging);
        if (committed == null)
            return "admin-safety-deposit-error-recovery";
        if (!committed.Value)
            return "admin-safety-deposit-error-stale";

        if (request.Action == AdminSafetyDepositAction.Withdraw && itemEntity is { } withdrawn)
        {
            if (!canContinue() || !TryGetAdminRecipient(admin, out _))
            {
                if (await RestoreAdminStoredChangeAsync(admin, box, replacement, audit))
                    return "admin-safety-deposit-error-recipient";

                staging.Retain = true;
                return "admin-safety-deposit-error-recovery";
            }

            // Once delivery begins, never recreate the DB record on an ambiguous game-world failure.
            // Keep the lock and staged entity for recovery instead of risking two copies.
            staging.Retain = true;
            if (!TryDeliverAdminItem(admin, withdrawn))
                return "admin-safety-deposit-error-recovery";

            ReleaseAdminStagedItem(withdrawn, staging);
            staging.Retain = false;
            audit.Result = "success";
            try
            {
                await _dbManager.CompleteSafetyDepositAdminAudit(audit.Id, audit.Result, audit.Details);
            }
            catch (Exception ex)
            {
                Log.Error($"Safety deposit item delivered for {audit.Id}, audit completion failed: {ex}");
                LogAdminBoxAction(admin, audit);
                return "admin-safety-deposit-success-audit-pending";
            }
        }

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

    private async Task<bool> RestoreAdminStoredChangeAsync(
        ICommonSession admin,
        WayfarerSafetyDepositBox original,
        List<string> replacement,
        SafetyDepositAdminAudit operation)
    {
        try
        {
            var current = await _dbManager.GetSafetyDepositBox(original.BoxId);
            if (current == null || current.LastWithdrawn != original.LastWithdrawn ||
                current.LastWithdrawnRoundId != original.LastWithdrawnRoundId ||
                current.OwnerUserId != original.OwnerUserId || current.CharacterIndex != original.CharacterIndex)
                return false;

            var remaining = new List<string>(replacement);
            foreach (var record in current.Items)
            {
                if (!remaining.Remove(record.EntityData))
                    return false;
            }

            if (remaining.Count != 0)
                return false;

            var data = new List<string>(original.Items.Count);
            foreach (var record in original.Items)
                data.Add(record.EntityData);

            var rollback = CreateAdminAudit(admin, original, "Rollback", "success",
                $"Restored operation {operation.Id}: recipient unavailable.");
            var committed = false;
            try
            {
                committed = await _dbManager.TryAdminReplaceSafetyDepositBoxItems(current, data, rollback);
            }
            catch (Exception ex)
            {
                Log.Error($"Safety deposit rollback {rollback.Id} failed; checking receipt: {ex}");
                committed = await _dbManager.GetSafetyDepositAdminAudit(rollback.Id) != null;
            }

            if (!committed)
                return false;

            LogAdminBoxAction(admin, rollback);
            // Failure to annotate the original receipt must not undo a confirmed rollback.
            try
            {
                await _dbManager.CompleteSafetyDepositAdminAudit(operation.Id, "rolled-back", operation.Details);
            }
            catch (Exception ex)
            {
                Log.Error($"Could not annotate rolled-back safety deposit operation {operation.Id}: {ex}");
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"Could not restore safety deposit operation {operation.Id}: {ex}");
            return false;
        }
    }

    private string DescribeAdminItem(EntityUid item)
    {
        var count = TryComp<StackComponent>(item, out var stack) ? stack.Count : 1;
        return $"{ToPrettyString(item)}, prototype {Prototype(item)?.ID}, count {count}";
    }
}
