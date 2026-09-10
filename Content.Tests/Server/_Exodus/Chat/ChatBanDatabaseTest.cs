using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared.CCVar;
using Content.Shared.Database;
using Content.Shared.Database._Exodus.Chat;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Moq;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Timing;
using Robust.UnitTesting;

namespace Content.Tests.Server._Exodus.Chat;

[TestFixture]
public sealed class ChatBanDatabaseTest
{
    private SqliteConnection _connection;
    private DbContextOptions<SqliteServerDbContext> _options;
    private IConfigurationManager _configuration;
    private LogManager _logs;
    private ServerDbSqlite _db;
    private readonly NetUserId _admin = new(Guid.NewGuid());
    private readonly NetUserId _target = new(Guid.NewGuid());

    [SetUp]
    public async Task SetUp()
    {
#if USE_SYSTEM_SQLITE
        SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
#endif
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        _options = new DbContextOptionsBuilder<SqliteServerDbContext>().UseSqlite(_connection).Options;
        _logs = new LogManager();
        _configuration = MockInterfaces.MakeConfigurationManager(new Mock<IGameTiming>().Object, _logs,
            loadCvarsFromTypes: [typeof(CCVars)]);
        _db = OpenDatabase();
        await _db.UpdatePlayerRecord(_admin, "Moderator", IPAddress.Loopback, null);
    }

    private ServerDbSqlite OpenDatabase() => new(() => _options, true, _configuration, true, _logs.GetSawmill("chat-ban-test"));

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
        _logs.Dispose();
    }

    private BanDef NewBan(BanType type = BanType.Chat, DateTimeOffset? expiry = null)
    {
        return new BanDef(null, type, [_target], [], [], DateTimeOffset.UtcNow, expiry, [], TimeSpan.FromHours(2),
            "Chat spam", NoteSeverity.Medium, _admin, null,
            roles: type == BanType.Role ? [new BanRoleDef("Job", "Passenger")] : null,
            chats: type == BanType.Chat ? [BannableChats.OOC, BannableChats.Dead] : null);
    }

    [Test]
    public async Task ChatBanSurvivesReopeningAndUsesGeneralBanHistory()
    {
        var ban = await _db.AddBanAsync(NewBan(expiry: DateTimeOffset.UtcNow.AddHours(1)));
        var reopened = OpenDatabase();
        var stored = await reopened.GetBanAsync(ban.Id!.Value);
        var note = await reopened.GetBanAsNoteAsync(ban.Id.Value);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Type, Is.EqualTo(BanType.Chat));
            Assert.That(stored.Chats, Is.EqualTo(ban.Chats));
            Assert.That(stored.ExpirationTime, Is.EqualTo(ban.ExpirationTime));
            Assert.That(stored.Roles, Is.Null);
            Assert.That(stored.UserIds, Does.Contain(_target));
            Assert.That(note.Type, Is.EqualTo(BanType.Chat));
            Assert.That(note.Chats, Is.EqualTo(ban.Chats));
            Assert.That(note.CreatedBy.UserId, Is.EqualTo(_admin));
        });
        Assert.That(await reopened.GetPlayerRecordByUserId(_target, default), Is.Null, "Offline accounts must not need a player record");
    }

    [Test]
    public async Task ChatRevocationIsIdempotentAndCannotRevokeServerOrRoleBans()
    {
        var chat = await _db.AddBanAsync(NewBan());
        var other = await _db.AddBanAsync(NewBan());
        var server = await _db.AddBanAsync(NewBan(BanType.Server));
        var role = await _db.AddBanAsync(NewBan(BanType.Role));
        var now = DateTimeOffset.UtcNow;
        Assert.That(await _db.TryPardonChatBanAsync(server.Id!.Value, _admin, now), Is.False);
        Assert.That(await _db.TryPardonChatBanAsync(role.Id!.Value, _admin, now), Is.False);
        Assert.That(await _db.TryPardonChatBanAsync(chat.Id!.Value, _admin, now), Is.True);
        Assert.That(await _db.TryPardonChatBanAsync(chat.Id.Value, _admin, now), Is.False);
        var stored = await _db.GetBanAsync(chat.Id.Value);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Unban.UnbanningAdmin, Is.EqualTo(_admin));
            Assert.That(stored.Unban.UnbanTime, Is.EqualTo(now));
            Assert.That(stored.Chats, Is.EqualTo(chat.Chats));
        });
        Assert.That((await _db.GetBanAsync(other.Id!.Value)).Unban, Is.Null);
    }

    [Test]
    public async Task ChatBansDoNotAffectServerOrRoleBanQueries()
    {
        var chat = await _db.AddBanAsync(NewBan());
        var role = await _db.AddBanAsync(NewBan(BanType.Role));
        Assert.That(await _db.GetBansAsync(null, _target, null, null, false, BanType.Server), Is.Empty);
        var roles = await _db.GetBansAsync(null, _target, null, null, false, BanType.Role);
        Assert.That(roles.Single().Id, Is.EqualTo(role.Id));
        Assert.That(roles.Single().Roles!.Value.Single(), Is.EqualTo(new BanRoleDef("Job", "Passenger")));
        var chats = await _db.GetBansAsync(null, _target, null, null, false, BanType.Chat);
        Assert.That(chats.Single().Id, Is.EqualTo(chat.Id));
    }

    [Test]
    public async Task ExpiredAndRevokedBansRemainInHistoryOnly()
    {
        var expired = await _db.AddBanAsync(NewBan(expiry: DateTimeOffset.UtcNow.AddMinutes(1)));
        await using (var context = new SqliteServerDbContext(_options))
        {
            var entity = await context.Ban.SingleAsync(b => b.Id == expired.Id);
            entity.BanTime = DateTime.UtcNow.AddHours(-2);
            entity.ExpirationTime = DateTime.UtcNow.AddHours(-1);
            await context.SaveChangesAsync();
        }

        var revoked = await _db.AddBanAsync(NewBan());
        await _db.TryPardonChatBanAsync(revoked.Id!.Value, _admin, DateTimeOffset.UtcNow);
        Assert.That(await _db.GetBansAsync(null, _target, null, null, false, BanType.Chat), Is.Empty);
        Assert.That(await _db.GetBansAsync(null, _target, null, null, true, BanType.Chat), Has.Count.EqualTo(2));
        Assert.That(await _db.GetAllAdminRemarks(_target.UserId), Has.Count.EqualTo(2));
    }

    [TestCase(BannableChats.Invalid)]
    [TestCase((BannableChats)255)]
    public void InvalidChannelsCannotEnterBanStorage(BannableChats chat)
    {
        Assert.Throws<ArgumentException>(() => new BanDef(null, BanType.Chat, [_target], [], [], DateTimeOffset.UtcNow,
            null, [], TimeSpan.Zero, "Spam", NoteSeverity.Medium, _admin, null, chats: [chat]));
    }

    [Test]
    public void InvalidReasonsAndDurationsCannotEnterBanStorage()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var reason in new[] { "", "   ", new string('x', ChatBanLimits.MaxReasonLength + 1) })
        {
            Assert.Throws<ArgumentException>(() => new BanDef(null, BanType.Chat, [_target], [], [], now, null,
                [], TimeSpan.Zero, reason, NoteSeverity.Medium, _admin, null, chats: [BannableChats.OOC]));
        }
        Assert.Throws<ArgumentException>(() => new BanDef(null, BanType.Chat, [_target], [], [], now, now.AddMinutes(-1),
            [], TimeSpan.Zero, "Spam", NoteSeverity.Medium, _admin, null, chats: [BannableChats.OOC]));
    }

    [Test]
    public void SqliteAndPostgresModelsMatchTheirMigrations()
    {
        using var sqlite = new SqliteServerDbContext(_options);
        using var postgres = new PostgresServerDbContext(new DbContextOptionsBuilder<PostgresServerDbContext>()
            .UseNpgsql("Host=localhost;Database=chat_bans_model_check").Options);
        Assert.Multiple(() =>
        {
            Assert.That(sqlite.Database.HasPendingModelChanges(), Is.False);
            Assert.That(postgres.Database.HasPendingModelChanges(), Is.False);
        });
    }
}
