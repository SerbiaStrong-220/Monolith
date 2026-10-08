using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._Exodus.SafetyDepositBox;
using Content.Shared.Storage;
using Robust.Shared.Player;

namespace Content.Server._WF.SafetyDepositBox;

public sealed partial class SafetyDepositBoxSystem
{
    private async Task<string> AdminModifyPhysicalItemAsync(
        ICommonSession admin,
        WayfarerSafetyDepositBox box,
        EntityUid physical,
        AdminSafetyDepositModifyMessage request,
        AdminHandItem? handItem,
        AdminBoxStaging staging,
        Func<bool> canContinue)
    {
        if (!TryComp<StorageComponent>(physical, out var storage))
            return "admin-safety-deposit-error-unavailable";

        _uiSystem.CloseUi(physical, StorageComponent.StorageUiKey.Key);
        EntityUid item;
        if (request.Action == AdminSafetyDepositAction.Add)
        {
            if (request.RecordId != 0 || request.Entity != null ||
                SpawnAdminItem(request.PrototypeId.Trim(), staging) is not { } added)
                return "admin-safety-deposit-error-invalid";

            item = added;
        }
        else if (request.Action == AdminSafetyDepositAction.AddFromHand)
        {
            if (handItem is not { } source || !IsAdminHandItemValid(admin, source))
                return "admin-safety-deposit-error-hand";

            item = source.Item;
        }
        else
        {
            if (request.Entity is not { } netEntity || !TryGetEntity(netEntity, out var entity) ||
                entity is not { } selected || TerminatingOrDeleted(selected) || !storage.Container.Contains(selected))
                return "admin-safety-deposit-error-stale";

            item = selected;
        }

        var audit = CreateAdminAudit(admin, box, request.Action.ToString(), "pending",
            $"{DescribeAdminItem(item)}; source: world; reason: {request.Reason.Trim()}", SaveAdminItem(item));
        // A durable intent is required before touching a physical item. It remains visible if completion fails.
        await _dbManager.AddSafetyDepositAdminAudit(audit);

        var result = "admin-safety-deposit-error-stale";
        var succeeded = false;
        var faulted = false;
        try
        {
            if (!canContinue() || !IsAdminPhysicalBoxValid(physical, box) || TerminatingOrDeleted(item) ||
                !GetAdminPhysicalBoxes().TryGetValue(box.BoxId, out var current) || current != physical ||
                !TryComp<StorageComponent>(physical, out storage))
                return result;

            _allowedBoxMutations.Add(box.BoxId);
            try
            {
                if (request.Action == AdminSafetyDepositAction.AddFromHand)
                {
                    if (handItem is not { } source || !IsAdminHandItemValid(admin, source))
                    {
                        result = "admin-safety-deposit-error-hand";
                        return result;
                    }

                    if (!TryInsertAdminHandItem((physical, storage), source))
                    {
                        result = "admin-safety-deposit-error-capacity";
                        return result;
                    }
                }
                else if (request.Action == AdminSafetyDepositAction.Add)
                {
                    if (!_storage.Insert(physical, item, out _, storageComp: storage,
                            playSound: false, stackAutomatically: false))
                    {
                        result = "admin-safety-deposit-error-capacity";
                        return result;
                    }

                    ReleaseAdminStagedItem(item, staging);
                }
                else
                {
                    if (!storage.Container.Contains(item))
                        return result;

                    if (request.Action == AdminSafetyDepositAction.Withdraw)
                    {
                        if (!TryGetAdminRecipient(admin, out var recipient))
                        {
                            result = "admin-safety-deposit-error-recipient";
                            return result;
                        }

                        if (!_container.Remove(item, storage.Container, destination: Transform(recipient).Coordinates))
                            return result;

                        _hands.TryPickupAnyHand(recipient, item);
                    }
                    else
                    {
                        // Detach before deferred deletion so the item cannot be included in a new deposit.
                        if (!_container.Remove(item, storage.Container))
                            return result;

                        _transform.DetachEntity(item);
                        QueueDel(item);
                    }
                }

                succeeded = true;
                result = "admin-safety-deposit-success";
            }
            finally
            {
                _allowedBoxMutations.Remove(box.BoxId);
            }
        }
        catch
        {
            // A callback may throw after moving the entity. Do not label an uncertain mutation as a clean failure.
            faulted = true;
            throw;
        }
        finally
        {
            audit.Result = faulted ? "pending" : succeeded ? "success" : "failed";
            LogAdminBoxAction(admin, audit);
            try
            {
                await _dbManager.CompleteSafetyDepositAdminAudit(audit.Id, audit.Result, audit.Details);
            }
            catch (Exception ex)
            {
                Log.Error($"Physical safety deposit operation {audit.Id} finished ({audit.Result}), audit completion failed: {ex}");
                if (succeeded)
                    result = "admin-safety-deposit-success-audit-pending";
            }
        }

        return result;
    }
}
