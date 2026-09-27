using System.IO;
using System.Threading.Tasks;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared.Database;
using Content.Shared.Storage;
using Content.Shared.Timing;
using Robust.Shared.Map;
using Robust.Shared.Network;

namespace Content.Server._WF.SafetyDepositBox;

public sealed partial class SafetyDepositBoxSystem
{
    [Dependency] private UseDelaySystem _useDelay = default!;

    private async Task WithdrawBoxAsync(
        EntityUid consoleUid,
        EntityUid player,
        NetUserId userId,
        int characterIndex,
        Guid boxId,
        bool reclaim = false)
    {
        EntityUid? boxEntity = null;
        var roundId = _gameTicker.RoundId;
        var databaseMutationAttempted = false;
        var succeeded = false;
        var retainStagedCopy = false;
        List<string>? storedData = null;
        var retainedData = new List<string>();
        var restoredItems = new List<EntityUid>();
        var overflowItems = new List<EntityUid>();

        try
        {
            var box = await _dbManager.GetSafetyDepositBox(boxId);
            if (box == null)
            {
                Reject(consoleUid, player, "safety-deposit-error-box-not-found");
                return;
            }

            if (box.OwnerUserId != userId.UserId || box.CharacterIndex != characterIndex)
            {
                Reject(consoleUid, player, "safety-deposit-error-not-owner");
                return;
            }

            if (reclaim)
            {
                // A lost physical box may still have unresolved item records from a partial withdrawal.
                if (box.LastWithdrawn == null ||
                    box.LastWithdrawnRoundId is not { } withdrawnRound || withdrawnRound == roundId)
                {
                    Reject(consoleUid, player, "safety-deposit-error-not-lost");
                    return;
                }
            }
            else if (box.LastWithdrawn != null)
            {
                Reject(consoleUid, player, "safety-deposit-error-already-withdrawn");
                return;
            }

            if (_gameTicker.RoundId != roundId ||
                !IsActorForCharacter(player, userId, characterIndex) ||
                !TryGetBoxPrototype(box.ProtoId, out var prototype))
            {
                Reject(consoleUid, player, "safety-deposit-error-invalid-box");
                return;
            }

            storedData = new List<string>(box.Items.Count);
            foreach (var item in box.Items)
                storedData.Add(item.EntityData);

            boxEntity = Spawn(prototype.ID, MapCoordinates.Nullspace);
            ConfigurePhysicalBox(boxEntity.Value, boxId, userId.UserId, characterIndex, MetaData(player).EntityName);
            TryComp<StorageComponent>(boxEntity.Value, out var storageComp);

            if (!string.IsNullOrEmpty(box.Nickname))
                _label.Label(boxEntity.Value, box.Nickname);

            _allowedBoxMutations.Add(boxId);
            try
            {
                foreach (var itemData in box.Items)
                {
                    EntityUid? itemEntity = null;
                    try
                    {
                        using var reader = new StringReader(itemData.EntityData);
                        if (!_loader.TryLoadEntity(reader, $"safety deposit box {boxId}, item {itemData.Id}", out var entity) ||
                            entity is not { } loaded || TerminatingOrDeleted(loaded.Owner))
                        {
                            throw new InvalidOperationException("Could not deserialize the stored item.");
                        }

                        itemEntity = loaded.Owner;
                        EnsureComp<SafetyDepositStoredComponent>(loaded.Owner);
                        ResetStoredUseDelays(loaded.Owner);

                        // Automatic stacking can report success after inserting only part of a stack.
                        // Keep each restored entity intact until its entire DB record can be consumed.
                        if (storageComp == null ||
                            !_storage.Insert(boxEntity.Value, loaded.Owner, out _, storageComp: storageComp,
                                playSound: false, stackAutomatically: false))
                        {
                            overflowItems.Add(loaded.Owner);
                        }

                        restoredItems.Add(loaded.Owner);
                    }
                    catch (Exception ex)
                    {
                        retainedData.Add(itemData.EntityData);
                        if (itemEntity is { } failed && !TerminatingOrDeleted(failed))
                            QueueDel(failed);

                        Log.Error($"Could not restore item record {itemData.Id} from safety deposit box {boxId}; its data will be retained: {ex}");
                    }
                }
            }
            finally
            {
                _allowedBoxMutations.Remove(boxId);
            }

            databaseMutationAttempted = true;
            await _dbManager.SetSafetyDepositBoxWithdrawnItems(boxId, roundId, retainedData);
            if (!await IsBoxWithdrawnAsync(boxId, userId.UserId, characterIndex, retainedData.Count))
                throw new InvalidOperationException($"Safety deposit box {boxId} did not reach the expected withdrawn state.");

            if (_gameTicker.RoundId != roundId ||
                !IsActorForCharacter(player, userId, characterIndex) ||
                !TryDeliverWithdrawnBox(boxEntity.Value, restoredItems, overflowItems, player, consoleUid))
            {
                throw new InvalidOperationException($"Could not deliver safety deposit box {boxId}.");
            }

            succeeded = true;
            NotifyBoxWithdrawal(consoleUid, player, retainedData.Count, overflowItems.Count, reclaim && storedData.Count == 0);
            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(player):actor} withdrew safety deposit box {boxId}: {restoredItems.Count} items restored, {overflowItems.Count} outside the box, {retainedData.Count} records retained for recovery (reclaim: {reclaim})");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to withdraw safety deposit box {boxId}: {ex}");
            if (!succeeded)
            {
                if (databaseMutationAttempted && storedData != null && !await RestoreStoredBoxAsync(boxId, storedData))
                {
                    // Never issue a second copy while the DB might still contain the original items.
                    // Preserve staged entities for admin recovery if neither DB state can be confirmed.
                    retainStagedCopy = true;
                    try
                    {
                        if (boxEntity is { } staged && _gameTicker.RoundId == roundId &&
                            await IsBoxWithdrawnAsync(boxId, userId.UserId, characterIndex, retainedData.Count) &&
                            TryDeliverWithdrawnBox(staged, restoredItems, overflowItems, player, consoleUid))
                        {
                            succeeded = true;
                            retainStagedCopy = false;
                        }
                    }
                    catch (Exception recoveryEx)
                    {
                        Log.Error($"Failed to verify or deliver staged safety deposit box {boxId}: {recoveryEx}");
                    }

                    _adminLogger.Add(LogType.Action, LogImpact.High,
                        $"Safety deposit withdrawal rollback failed for box {boxId}; staged box {boxEntity} retained (delivered: {succeeded}).");
                }

                if (succeeded)
                    NotifyBoxWithdrawal(consoleUid, player, retainedData.Count, overflowItems.Count, reclaim && storedData?.Count == 0);
                else
                    Reject(consoleUid, player, "safety-deposit-error-transaction");
            }
        }
        finally
        {
            if (!succeeded && !retainStagedCopy)
            {
                foreach (var item in restoredItems)
                {
                    if (!TerminatingOrDeleted(item))
                        QueueDel(item);
                }

                if (boxEntity is { } spawned && !TerminatingOrDeleted(spawned))
                    QueueDel(spawned);
            }

            // An ambiguous DB failure must not allow a second withdrawal of the staged copy.
            if (!retainStagedCopy)
                _activeBoxOperations.Remove(boxId);

            UpdateUIIfOpen(consoleUid, player);
        }
    }

    private void ResetStoredUseDelays(EntityUid item)
    {
        // Saved use delays contain timestamps from the previous server run, including on nested items.
        var pending = new Stack<EntityUid>();
        var delayQuery = GetEntityQuery<UseDelayComponent>();
        var transformQuery = GetEntityQuery<TransformComponent>();
        pending.Push(item);

        while (pending.TryPop(out var current))
        {
            if (TerminatingOrDeleted(current) || !transformQuery.TryGetComponent(current, out var transform))
                continue;

            if (delayQuery.TryGetComponent(current, out var delay))
                _useDelay.ResetAllDelays((current, delay));

            var children = transform.ChildEnumerator;
            while (children.MoveNext(out var child))
                pending.Push(child);
        }
    }

    private void NotifyBoxWithdrawal(EntityUid consoleUid, EntityUid player, int retainedCount, int overflowCount, bool emptyReclaim)
    {
        if (retainedCount > 0)
            Popup(player, "safety-deposit-withdraw-partial", ("count", retainedCount));
        else
            Popup(player, emptyReclaim ? "safety-deposit-reclaim-success" : "safety-deposit-withdraw-success");

        if (overflowCount > 0)
            Popup(player, "safety-deposit-withdraw-overflow", ("count", overflowCount));

        Confirm(consoleUid);
    }

    private bool TryDeliverWithdrawnBox(
        EntityUid boxEntity,
        List<EntityUid> restoredItems,
        List<EntityUid> overflowItems,
        EntityUid player,
        EntityUid consoleUid)
    {
        if (TerminatingOrDeleted(boxEntity))
            return false;

        foreach (var item in restoredItems)
        {
            if (TerminatingOrDeleted(item))
                return false;
        }

        var recipient = !TerminatingOrDeleted(player) ? player : consoleUid;
        if (TerminatingOrDeleted(recipient) || Transform(recipient).MapID == MapId.Nullspace)
            return false;

        var coordinates = Transform(recipient).Coordinates;
        foreach (var item in overflowItems)
        {
            _transform.SetCoordinates(item, coordinates);
            _transform.SetLocalRotation(item, Angle.Zero);
        }

        return TryDeliverBox(boxEntity, player, consoleUid);
    }
}
