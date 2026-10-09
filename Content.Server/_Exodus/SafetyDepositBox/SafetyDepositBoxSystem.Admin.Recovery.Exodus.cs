using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Database._Exodus.SafetyDepositBox;
using Content.Shared._Exodus.SafetyDepositBox;
using Content.Shared.GameTicking;
using Robust.Shared.Map;
using Robust.Shared.Player;

namespace Content.Server._WF.SafetyDepositBox;

public sealed partial class SafetyDepositBoxSystem
{
    // Tracks ownership of asynchronous locks, not gameplay state. Old callbacks must not release new locks.
    private readonly Dictionary<Guid, AdminBoxStaging> _adminBoxOperations = [];

    private void InitializeAdminRecovery()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnAdminRoundRestart);
    }

    private void OnAdminRoundRestart(RoundRestartCleanupEvent args)
    {
        // The world, including retained nullspace staging, is about to be deleted.
        // Only admin-owned locks belong to this registry; player operations manage their own locks.
        foreach (var boxId in _adminBoxOperations.Keys)
        {
            _activeBoxOperations.Remove(boxId);
            _allowedBoxMutations.Remove(boxId);
        }

        _adminBoxOperations.Clear();
    }

    private bool IsAdminOperationCurrent(Guid boxId, AdminBoxStaging staging)
    {
        return _adminBoxOperations.TryGetValue(boxId, out var current) && ReferenceEquals(current, staging);
    }

    private void FinishAdminBoxOperation(Guid boxId, AdminBoxStaging staging)
    {
        var current = IsAdminOperationCurrent(boxId, staging);
        if (current && staging.Retain)
        {
            Log.Error($"Safety deposit box {boxId} remains locked after an uncertain admin operation; staged entities: {string.Join(", ", staging.Entities)}");
        }
        else
        {
            foreach (var entity in staging.Entities)
            {
                if (!TerminatingOrDeleted(entity))
                    TryQueueDel(entity);
            }

            if (current)
            {
                _adminBoxOperations.Remove(boxId);
                _activeBoxOperations.Remove(boxId);
            }
        }

        RefreshPlayerSafetyDepositUis();
    }

    private async Task<string> AdminWithdrawStoredItemAsync(
        ICommonSession admin,
        WayfarerSafetyDepositBox box,
        List<string> replacement,
        EntityUid item,
        string itemData,
        string details,
        AdminBoxStaging staging,
        Func<bool> canContinue)
    {
        var audit = CreateAdminAudit(admin, box, "WithdrawStored", "prepared", details, itemData);
        var deliveryStarted = false;
        try
        {
            var committed = await CommitAdminStoredChangeAsync(box, replacement, audit, staging);
            if (committed == null)
                return "admin-safety-deposit-error-recovery";
            if (!committed.Value)
                return "admin-safety-deposit-error-stale";

            LogAdminBoxAction(admin, audit);
            if (!canContinue() || !TryGetAdminRecipient(admin, out _))
                return await CancelAdminWithdrawalBeforeDeliveryAsync(admin, box, audit, staging);

            // A crash before this transition is safe to restore. Afterwards delivery is uncertain until confirmed.
            if (!await _dbManager.TryTransitionSafetyDepositAdminWithdrawal(audit.Id, "prepared", "delivering"))
                return "admin-safety-deposit-error-recovery-unavailable";

            audit.Result = "delivering";
            if (!canContinue() || !TryGetAdminRecipient(admin, out _))
                return await CancelAdminWithdrawalBeforeDeliveryAsync(admin, box, audit, staging);

            deliveryStarted = true;
            if (!TryDeliverAdminItem(admin, item))
                return "admin-safety-deposit-error-recovery";

            ReleaseAdminStagedItem(item, staging);
            try
            {
                if (!await _dbManager.TryTransitionSafetyDepositAdminWithdrawal(audit.Id, "delivering", "success"))
                    return "admin-safety-deposit-success-audit-pending";
            }
            catch (Exception ex)
            {
                Log.Error($"Safety deposit item delivered for {audit.Id}, audit completion failed: {ex}");
                return "admin-safety-deposit-success-audit-pending";
            }

            audit.Result = "success";
            LogAdminBoxAction(admin, audit);
            return "admin-safety-deposit-success";
        }
        catch (Exception ex)
        {
            Log.Error($"Safety deposit withdrawal {audit.Id} requires recovery: {ex}");
            return "admin-safety-deposit-error-recovery";
        }
        finally
        {
            // This is a deserialized copy. Its YAML remains in either the original row or the atomic audit.
            // Retaining its lock cannot improve recovery. Preserve a possibly delivered world entity;
            // otherwise discard staging and let the durable receipt drive recovery, including after restart.
            if (deliveryStarted && !TerminatingOrDeleted(item) && Transform(item).MapID != MapId.Nullspace)
                ReleaseAdminStagedItem(item, staging);

            staging.Retain = false;
        }
    }

    private async Task<string> CancelAdminWithdrawalBeforeDeliveryAsync(
        ICommonSession admin,
        WayfarerSafetyDepositBox box,
        SafetyDepositAdminAudit operation,
        AdminBoxStaging staging)
    {
        // A round reset relinquishes this lock. Leave recovery to a new request instead of writing
        // after another operation may have acquired the box and read its snapshot.
        if (!IsAdminOperationCurrent(box.BoxId, staging))
            return "admin-safety-deposit-error-recovery";

        // This operation knows no world handoff occurred, even if permissions or the recipient changed.
        // Compensate atomically rather than rewriting an old snapshot or annotating the source separately.
        var resolution = CreateAdminAudit(admin, box, "RestoreWithdrawal", "success",
            $"Restored operation {operation.Id} before delivery: recipient or request unavailable.");
        return await CommitAdminWithdrawalResolutionAsync(admin, operation, resolution, true) == true
            ? "admin-safety-deposit-error-recipient"
            : "admin-safety-deposit-error-recovery";
    }

    /// <summary>
    /// Resolves a durable withdrawal receipt. A prepared withdrawal never started delivery; all other
    /// unfinished receipts need an explicit decision, and restoring them additionally requires Spawn.
    /// </summary>
    public async Task<string> AdminResolveWithdrawalAsync(
        ICommonSession admin,
        Guid ownerId,
        AdminSafetyDepositResolveRecoveryMessage request,
        Func<bool> canContinue)
    {
        if (!CanAdminModify(admin, AdminSafetyDepositAction.Withdraw, canContinue))
            return "admin-safety-deposit-error-permission";

        if (request.OperationId == Guid.Empty || string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            return "admin-safety-deposit-error-invalid";

        if (!_activeBoxOperations.Add(request.BoxId))
            return "admin-safety-deposit-error-busy";

        var staging = new AdminBoxStaging();
        _adminBoxOperations.Add(request.BoxId, staging);
        bool CanContinue() => IsAdminOperationCurrent(request.BoxId, staging) &&
                              CanAdminModify(admin, AdminSafetyDepositAction.Withdraw, canContinue);
        try
        {
            var operation = await _dbManager.GetSafetyDepositAdminAudit(request.OperationId);
            if (!CanContinue())
                return "admin-safety-deposit-error-permission";

            if (operation == null || operation.BoxId != request.BoxId || operation.OwnerUserId != ownerId ||
                !(operation.Action == "WithdrawStored" && operation.Result is "prepared" or "delivering" ||
                  operation.Action == "Withdraw" && operation.Result == "pending"))
            {
                return "admin-safety-deposit-error-recovery-unavailable";
            }

            var prepared = operation.Action == "WithdrawStored" && operation.Result == "prepared";
            if (prepared && !request.Restore)
                return "admin-safety-deposit-error-invalid";

            var requiredAction = request.Restore && !prepared
                ? AdminSafetyDepositAction.Add
                : AdminSafetyDepositAction.Withdraw;
            var box = await _dbManager.GetSafetyDepositBox(request.BoxId);
            if (!CanAdminModify(admin, requiredAction, CanContinue))
                return "admin-safety-deposit-error-permission";

            if (box == null || box.OwnerUserId != ownerId || box.CharacterIndex != operation.CharacterIndex)
                return "admin-safety-deposit-error-box";

            var resolution = CreateAdminAudit(admin, box,
                request.Restore ? "RestoreWithdrawal" : "ConfirmWithdrawal", "success",
                $"Resolved operation {operation.Id} ({operation.Action}/{operation.Result}); reason: {request.Reason.Trim()}");
            var resolved = await CommitAdminWithdrawalResolutionAsync(admin, operation, resolution, request.Restore);
            return resolved switch
            {
                true => request.Restore ? "admin-safety-deposit-recovery-restored" : "admin-safety-deposit-recovery-confirmed",
                false => "admin-safety-deposit-error-recovery-unavailable",
                null => "admin-safety-deposit-error-recovery",
            };
        }
        catch (Exception ex)
        {
            Log.Error($"Could not resolve safety deposit withdrawal {request.OperationId}: {ex}");
            return "admin-safety-deposit-error-database";
        }
        finally
        {
            FinishAdminBoxOperation(request.BoxId, staging);
        }
    }

    private async Task<bool?> CommitAdminWithdrawalResolutionAsync(
        ICommonSession admin,
        SafetyDepositAdminAudit operation,
        SafetyDepositAdminAudit resolution,
        bool restore)
    {
        try
        {
            if (!await _dbManager.TryResolveSafetyDepositAdminWithdrawal(operation.Id, operation.Result, restore, resolution))
                return false;
        }
        catch (Exception ex)
        {
            Log.Error($"Safety deposit recovery {resolution.Id} failed; checking its durable receipt: {ex}");
            try
            {
                if (await _dbManager.GetSafetyDepositAdminAudit(resolution.Id) == null)
                    return null;
            }
            catch (Exception receiptError)
            {
                Log.Error($"Safety deposit recovery {resolution.Id} is ambiguous: {receiptError}");
                return null;
            }
        }

        LogAdminBoxAction(admin, resolution);
        return true;
    }
}
