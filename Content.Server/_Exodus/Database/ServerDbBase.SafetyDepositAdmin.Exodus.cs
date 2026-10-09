using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database._Exodus.SafetyDepositBox;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    /// <summary>Lists boxes for the supplied owners across all character slots, without loading YAML.</summary>
    public async Task<List<SafetyDepositAdminSummary>> GetAdminSafetyDepositBoxes(
        List<Guid> ownerIds,
        CancellationToken cancel = default)
    {
        if (ownerIds.Count == 0)
            return new List<SafetyDepositAdminSummary>();

        await using var db = await GetDb(cancel);
        return await db.DbContext.WayfarerSafetyDepositBox
            .AsNoTracking()
            .Where(b => ownerIds.Contains(b.OwnerUserId))
            .OrderBy(b => b.OwnerUserId)
            .ThenBy(b => b.CharacterIndex)
            .ThenBy(b => b.BoxId)
            .Select(b => new SafetyDepositAdminSummary
            {
                BoxId = b.BoxId,
                OwnerUserId = b.OwnerUserId,
                CharacterIndex = b.CharacterIndex,
                OwnerName = b.OwnerName,
                ProtoId = b.ProtoId,
                Nickname = b.Nickname,
                LastWithdrawn = b.LastWithdrawn,
                LastWithdrawnRoundId = b.LastWithdrawnRoundId,
                ItemCount = b.Items.Count,
            })
            .ToListAsync(cancel);
    }

    /// <summary>Persists an operation before a potentially destructive external action.</summary>
    public async Task AddSafetyDepositAdminAudit(SafetyDepositAdminAudit audit, CancellationToken cancel = default)
    {
        if (audit.Id == Guid.Empty)
            throw new ArgumentException("An audit requires a caller-assigned operation identifier.", nameof(audit));

        await using var db = await GetDb(cancel);
        db.DbContext.SafetyDepositAdminAudits.Add(audit);
        await db.DbContext.SaveChangesAsync(cancel);
    }

    /// <summary>
    /// Updates physical and general audit outcomes while retaining recovery payloads.
    /// Stored withdrawals and finalized resolutions are left unchanged, even by late callbacks.
    /// </summary>
    public async Task CompleteSafetyDepositAdminAudit(
        Guid operationId,
        string result,
        string details,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var affected = await db.DbContext.SafetyDepositAdminAudits
            .Where(a => a.Id == operationId && a.Action != "WithdrawStored" &&
                        a.Result != "recovered" && a.Result != "confirmed")
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Result, result)
                .SetProperty(a => a.Details, details), cancel);
        if (affected == 0 && !await db.DbContext.SafetyDepositAdminAudits.AnyAsync(a => a.Id == operationId, cancel))
            throw new InvalidOperationException($"Safety deposit operation {operationId} no longer exists.");
    }

    /// <summary>Returns the full journal entry, including YAML for recovery after an uncertain commit.</summary>
    public async Task<SafetyDepositAdminAudit?> GetSafetyDepositAdminAudit(
        Guid operationId,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.SafetyDepositAdminAudits
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == operationId, cancel);
    }

    /// <summary>Returns at most 200 latest audit entries without loading their recovery YAML.</summary>
    public async Task<List<SafetyDepositAdminAudit>> GetSafetyDepositAdminAudits(
        Guid boxId,
        int limit = 50,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.SafetyDepositAdminAudits
            .AsNoTracking()
            .Where(a => a.BoxId == boxId)
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Take(Math.Clamp(limit, 1, 200))
            .Select(a => new SafetyDepositAdminAudit
            {
                Id = a.Id,
                AdminUserId = a.AdminUserId,
                AdminName = a.AdminName,
                OwnerUserId = a.OwnerUserId,
                CharacterIndex = a.CharacterIndex,
                BoxId = a.BoxId,
                CreatedAt = a.CreatedAt,
                Action = a.Action,
                Result = a.Result,
                Details = a.Details,
                RoundId = a.RoundId,
            })
            .ToListAsync(cancel);
    }

    /// <summary>
    /// Atomically advances a stored withdrawal toward delivery. Finalized or stale receipts cannot advance.
    /// A successful transition authorizes this operation's handoff, never replay of an earlier handoff.
    /// </summary>
    public async Task<bool> TryTransitionSafetyDepositAdminWithdrawal(
        Guid operationId,
        string expectedResult,
        string result,
        CancellationToken cancel = default)
    {
        if (operationId == Guid.Empty)
            throw new ArgumentException("A withdrawal requires an operation identifier.", nameof(operationId));

        if (!(expectedResult == "prepared" && result == "delivering") &&
            !(expectedResult == "delivering" && result == "success"))
        {
            throw new ArgumentException("Only prepared-to-delivering and delivering-to-success transitions are allowed.", nameof(result));
        }

        await using var db = await GetDb(cancel);
        return await db.DbContext.SafetyDepositAdminAudits
            .Where(a => a.Id == operationId && a.Action == "WithdrawStored" && a.Result == expectedResult)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Result, result), cancel) == 1;
    }

    /// <summary>
    /// Lists every unfinished withdrawal for a box without loading recovery payloads or hiding old entries.
    /// Legacy pending withdrawals remain uncertain regardless of their human-readable source details.
    /// </summary>
    public async Task<List<SafetyDepositAdminAudit>> GetSafetyDepositAdminRecoveries(
        Guid boxId,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        return await db.DbContext.SafetyDepositAdminAudits
            .AsNoTracking()
            .Where(a => a.BoxId == boxId &&
                        (a.Action == "WithdrawStored" && (a.Result == "prepared" || a.Result == "delivering") ||
                         a.Action == "Withdraw" && a.Result == "pending"))
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.Id)
            .Select(a => new SafetyDepositAdminAudit
            {
                Id = a.Id,
                AdminUserId = a.AdminUserId,
                AdminName = a.AdminName,
                OwnerUserId = a.OwnerUserId,
                CharacterIndex = a.CharacterIndex,
                BoxId = a.BoxId,
                CreatedAt = a.CreatedAt,
                Action = a.Action,
                Result = a.Result,
                Details = a.Details,
                RoundId = a.RoundId,
            })
            .ToListAsync(cancel);
    }

    /// <summary>
    /// Resolves one unfinished withdrawal exactly once. Restoration appends its original item to the
    /// current matching box without replacing later contents or changing box status. The source receipt,
    /// appended item, and resolution receipt commit together. Rights and explicit uncertainty confirmation
    /// must be checked by the calling system; another administrator may resolve the original operation.
    /// </summary>
    public async Task<bool> TryResolveSafetyDepositAdminWithdrawal(
        Guid operationId,
        string expectedResult,
        bool restore,
        SafetyDepositAdminAudit resolution,
        CancellationToken cancel = default)
    {
        if (operationId == Guid.Empty || resolution.Id == Guid.Empty || resolution.Id == operationId ||
            resolution.AdminUserId == Guid.Empty || string.IsNullOrWhiteSpace(resolution.AdminName) ||
            resolution.Action != (restore ? "RestoreWithdrawal" : "ConfirmWithdrawal") || resolution.Result != "success")
        {
            throw new ArgumentException("A resolution requires a distinct operation identifier, actor, and matching action and outcome.", nameof(resolution));
        }

        await using var db = await GetDb(cancel);
        await using var transaction = await db.DbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancel);
        if (await db.DbContext.SafetyDepositAdminAudits.AnyAsync(a => a.Id == resolution.Id, cancel))
            return false;

        var original = await db.DbContext.SafetyDepositAdminAudits
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == operationId, cancel);
        if (original == null || original.Result != expectedResult ||
            !(original.Action == "WithdrawStored" && (original.Result == "prepared" || original.Result == "delivering") ||
              original.Action == "Withdraw" && original.Result == "pending") ||
            !restore && original.Result == "prepared")
        {
            return false;
        }

        if (resolution.BoxId != original.BoxId || resolution.OwnerUserId != original.OwnerUserId ||
            resolution.CharacterIndex != original.CharacterIndex)
        {
            throw new ArgumentException("The resolution receipt must identify the original box owner and character slot.", nameof(resolution));
        }

        WayfarerSafetyDepositBox? box = null;
        if (restore)
        {
            if (string.IsNullOrWhiteSpace(original.ItemData))
                return false;

            box = await db.DbContext.WayfarerSafetyDepositBox.SingleOrDefaultAsync(
                b => b.BoxId == original.BoxId && b.OwnerUserId == original.OwnerUserId &&
                     b.CharacterIndex == original.CharacterIndex, cancel);
            if (box == null)
                return false;
        }

        var affected = await db.DbContext.SafetyDepositAdminAudits
            .Where(a => a.Id == operationId && a.Action == original.Action && a.Result == expectedResult)
            .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Result, restore ? "recovered" : "confirmed"), cancel);
        if (affected != 1)
            return false;

        if (box != null)
        {
            db.DbContext.WayfarerSafetyDepositBoxItem.Add(new WayfarerSafetyDepositBoxItem
            {
                BoxId = box.Id,
                EntityData = original.ItemData!,
                DepositDate = DateTime.UtcNow,
            });
        }

        db.DbContext.SafetyDepositAdminAudits.Add(resolution);
        await db.DbContext.SaveChangesAsync(cancel);
        await transaction.CommitAsync(cancel);
        return true;
    }

    /// <summary>
    /// Replaces item rows only if the box and its entire item snapshot still match.
    /// Box status is preserved, and the recovery audit is committed in the same transaction.
    /// A duplicate operation identifier cannot perform the replacement again.
    /// </summary>
    public async Task<bool> TryAdminReplaceSafetyDepositBoxItems(
        WayfarerSafetyDepositBox expected,
        List<string> replacementData,
        SafetyDepositAdminAudit audit,
        CancellationToken cancel = default)
    {
        if (audit.Id == Guid.Empty || audit.BoxId != expected.BoxId ||
            audit.OwnerUserId != expected.OwnerUserId || audit.CharacterIndex != expected.CharacterIndex)
        {
            throw new ArgumentException("The audit must identify this operation and the expected box owner and slot.", nameof(audit));
        }

        await using var db = await GetDb(cancel);
        await using var transaction = await db.DbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancel);
        if (await db.DbContext.SafetyDepositAdminAudits.AnyAsync(a => a.Id == audit.Id, cancel))
            return false;

        var box = await db.DbContext.WayfarerSafetyDepositBox
            .Include(b => b.Items)
            .SingleOrDefaultAsync(b => b.BoxId == expected.BoxId, cancel);
        if (box == null || box.Id != expected.Id || box.OwnerUserId != expected.OwnerUserId ||
            box.CharacterIndex != expected.CharacterIndex || box.LastWithdrawn != expected.LastWithdrawn ||
            box.LastWithdrawnRoundId != expected.LastWithdrawnRoundId || box.Items.Count != expected.Items.Count)
        {
            return false;
        }

        var expectedItems = new Dictionary<int, string>(expected.Items.Count);
        foreach (var item in expected.Items)
        {
            if (!expectedItems.TryAdd(item.Id, item.EntityData))
                return false;
        }

        foreach (var item in box.Items)
        {
            if (!expectedItems.TryGetValue(item.Id, out var entityData) || item.EntityData != entityData)
                return false;
        }

        db.DbContext.WayfarerSafetyDepositBoxItem.RemoveRange(box.Items);
        var depositDate = DateTime.UtcNow;
        foreach (var entityData in replacementData)
        {
            db.DbContext.WayfarerSafetyDepositBoxItem.Add(new WayfarerSafetyDepositBoxItem
            {
                BoxId = box.Id,
                EntityData = entityData,
                DepositDate = depositDate,
            });
        }

        db.DbContext.SafetyDepositAdminAudits.Add(audit);
        await db.DbContext.SaveChangesAsync(cancel);
        await transaction.CommitAsync(cancel);
        return true;
    }
}
