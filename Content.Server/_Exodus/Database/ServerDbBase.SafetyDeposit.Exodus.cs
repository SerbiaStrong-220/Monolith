using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    /// <summary>
    /// Marks a box as withdrawn and keeps only the item data that could not be delivered.
    /// Both changes are committed together, including when compensating a failed deposit.
    /// </summary>
    public async Task SetSafetyDepositBoxWithdrawnItems(
        Guid boxId,
        int roundId,
        List<string> retainedData,
        CancellationToken cancel = default)
    {
        await using var db = await GetDb(cancel);
        var box = await db.DbContext.WayfarerSafetyDepositBox
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.BoxId == boxId, cancel);

        if (box == null)
            throw new InvalidOperationException($"Safety deposit box {boxId} no longer exists.");

        db.DbContext.WayfarerSafetyDepositBoxItem.RemoveRange(box.Items);
        foreach (var entityData in retainedData)
        {
            box.Items.Add(new WayfarerSafetyDepositBoxItem
            {
                BoxId = box.Id,
                EntityData = entityData,
                DepositDate = DateTime.UtcNow,
            });
        }

        box.LastWithdrawn = DateTime.UtcNow;
        box.LastWithdrawnRoundId = roundId;
        await db.DbContext.SaveChangesAsync(cancel);
    }
}
