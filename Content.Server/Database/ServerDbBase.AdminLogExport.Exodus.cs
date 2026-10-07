// Exodus: bounded, cancellation-aware log export queries independent from UI filters and limits.
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._Exodus.Administration.LogExport;
using Content.Shared._Exodus.Administration.LogExport;
using Content.Shared.Administration.Logs;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public async Task<AdminLogExportSnapshot?> GetAdminLogExportSnapshot(int roundId, CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        if (roundId <= 0)
            return null;

        await using var db = await GetDb(cancel);
        // One database statement distinguishes a missing round from an existing empty round.
        var boundary = await db.DbContext.Round.AsNoTracking()
            .Where(round => round.Id == roundId)
            .Select(round => new
            {
                Count = round.AdminLogs.Count(),
                LastLogId = round.AdminLogs.Max(log => (int?) log.Id) ?? 0,
            })
            .SingleOrDefaultAsync(cancel);

        return boundary == null
            ? null
            : new AdminLogExportSnapshot(roundId, boundary.Count, boundary.LastLogId, DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<SharedAdminLog>> GetAdminLogExportPage(
        int roundId,
        int afterId,
        int lastLogId,
        int limit,
        CancellationToken cancel = default)
    {
        cancel.ThrowIfCancellationRequested();
        AdminLogExportDataBounds.ValidatePage(roundId, afterId, lastLogId, limit);
        await using var db = await GetDb(cancel);
        var query = db.DbContext.AdminLog.AsNoTracking()
            .Where(log => log.RoundId == roundId && log.Id > afterId && log.Id <= lastLogId)
            .OrderBy(log => log.Id);

        // Inspect only scalar sizes first: a malformed historical row must not allocate an arbitrary
        // message or player array before the export's limits can reject it.
        var sizes = await query.Take(limit)
            .Select(log => new { log.Id, Characters = log.Message.Length, Players = log.Players.Count })
            .ToListAsync(cancel);
        var characters = 0;
        var players = 0;
        var boundedLastId = afterId;
        foreach (var row in sizes)
        {
            AdminLogExportDataBounds.ValidateRecordSize(row.Characters, row.Players);
            if (boundedLastId != afterId &&
                (characters + row.Characters > AdminLogExportLimits.RecordBytes ||
                 players + row.Players > AdminLogExportDataBounds.MaxPagePlayers))
                break;

            characters += row.Characters;
            players += row.Players;
            boundedLastId = row.Id;
        }

        if (boundedLastId == afterId)
            return [];

        // Do not select Json or Player entities: export contains the same data as the admin log panel.
        // Split the collection query so a large Message is not repeated for every related player.
        var rows = await query.Where(log => log.Id <= boundedLastId)
            .AsSplitQuery()
            .Take(limit)
            .Select(log => new
            {
                log.Id,
                log.Type,
                log.Impact,
                log.Date,
                log.Message,
                Players = log.Players.Select(player => player.PlayerUserId).ToArray(),
            })
            .ToListAsync(cancel);
        var result = new List<SharedAdminLog>(rows.Count);
        var messageBytes = 0;
        foreach (var row in rows)
        {
            var log = new SharedAdminLog(row.Id, row.Type, row.Impact,
                NormalizeDatabaseTime(row.Date), row.Message, row.Players);
            var bytes = AdminLogExportDataBounds.GetMessageBytes(log);
            if (result.Count > 0 && messageBytes + bytes > AdminLogExportLimits.RecordBytes)
                break;

            messageBytes += bytes;
            result.Add(log);
        }

        return result;
    }
}
