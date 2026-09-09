// Exodus: atomic, idempotent revocation for the SS220 chat ban port.
using System.Threading.Tasks;
using Content.Shared.Database;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Network;

namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    public async Task<bool> TryPardonChatBanAsync(int banId, NetUserId? admin, DateTimeOffset time)
    {
        await using var db = await GetDb();
        if (!await db.DbContext.Ban.AnyAsync(b => b.Id == banId && b.Type == BanType.Chat && b.Unban == null))
            return false;

        db.DbContext.Unban.Add(new Unban
        {
            BanId = banId,
            UnbanningAdmin = admin?.UserId,
            UnbanTime = time.UtcDateTime,
        });
        try
        {
            await db.DbContext.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateException)
        {
            // Another server may have inserted the unique ban_id while this operation was awaiting the database.
            if (await db.DbContext.Unban.AnyAsync(b => b.BanId == banId))
                return false;
            throw;
        }
    }
}
