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

    /// <summary>Updates the outcome while retaining the original operation and recovery payload.</summary>
    public async Task CompleteSafetyDepositAdminAudit(
        Guid operationId,
        string result,
        string details,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var audit = await db.DbContext.SafetyDepositAdminAudits.SingleOrDefaultAsync(a => a.Id == operationId, cancel);
        if (audit == null)
            throw new InvalidOperationException($"Safety deposit operation {operationId} no longer exists.");

        audit.Result = result;
        audit.Details = details;
        await db.DbContext.SaveChangesAsync(cancel);
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
