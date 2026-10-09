// (c) Space Exodus Team - EXDS-RL with CLA
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Database._Exodus.SafetyDepositBox;
using Content.Shared.CCVar;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Timing;
using Robust.UnitTesting;

namespace Content.Tests.Server._Exodus.SafetyDepositBox;

[TestFixture]
public sealed class SafetyDepositAdminDatabaseTest
{
    private SqliteConnection _connection;
    private DbContextOptions<SqliteServerDbContext> _options;
    private IConfigurationManager _configuration;
    private LogManager _logs;
    private ServerDbSqlite _database;
    private ReaderColumnCapture _capture;

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
        _configuration.SetCVar(CCVars.DatabaseSqliteDelay, 0);
        _configuration.SetCVar(CCVars.DatabaseSqliteConcurrency, 1);
        _database = OpenDatabase();
        _capture = new ReaderColumnCapture();
        _options = new DbContextOptionsBuilder<SqliteServerDbContext>(_options).AddInterceptors(_capture).Options;
    }

    [TearDown]
    public void TearDown()
    {
        _connection.Dispose();
        _logs.Dispose();
    }

    [Test]
    public async Task OwnerSummaryIncludesAllCharacterSlotsWithoutReadingEntityYaml()
    {
        var owner = Guid.NewGuid();
        var anotherOwner = Guid.NewGuid();
        var first = await AddBox(owner, 0, "deliberately invalid YAML");
        var second = await AddBox(owner, 7, new string('x', 256 * 1024), "second record");
        await AddBox(anotherOwner, 0, "other owner's item");
        _capture.Columns.Clear();

        var summaries = await _database.GetAdminSafetyDepositBoxes([owner]);
        Assert.Multiple(() =>
        {
            Assert.That(summaries.Select(box => box.BoxId), Is.EquivalentTo(new[] { first.BoxId, second.BoxId }));
            Assert.That(summaries.Select(box => box.CharacterIndex), Is.EqualTo(new[] { 0, 7 }));
            Assert.That(summaries.Select(box => box.ItemCount), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(summaries.All(box => box.OwnerUserId == owner), Is.True);
            Assert.That(summaries[0].OwnerName, Is.EqualTo(first.OwnerName));
            Assert.That(summaries[0].ProtoId, Is.EqualTo(first.ProtoId));
            Assert.That(summaries[0].Nickname, Is.EqualTo(first.Nickname));
            Assert.That(_capture.Columns, Is.Not.Empty);
            Assert.That(_capture.Columns.SelectMany(columns => columns),
                Does.Not.Contain("entity_data"), "Listing boxes must not read serialized item payloads.");
        });

        Assert.That(await _database.GetAdminSafetyDepositBoxes([]), Is.Empty);
    }

    [Test]
    public async Task ItemRemovalPreservesWithdrawnBoxAndUndeliverableRecoveryRecords()
    {
        var original = await AddBox(Guid.NewGuid(), 3, "restorable item", "corrupt recovery YAML");
        await using (var context = new SqliteServerDbContext(_options))
        {
            var box = await context.WayfarerSafetyDepositBox.SingleAsync(b => b.BoxId == original.BoxId);
            box.LastWithdrawn = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
            box.LastWithdrawnRoundId = 42;
            await context.SaveChangesAsync();
        }

        original = await _database.GetSafetyDepositBox(original.BoxId);
        var audit = CreateAudit(original, "restorable item");
        Assert.That(await _database.TryAdminReplaceSafetyDepositBoxItems(original, ["corrupt recovery YAML"], audit), Is.True);

        var reopened = OpenDatabase();
        var updated = await reopened.GetSafetyDepositBox(original.BoxId);
        var receipt = await reopened.GetSafetyDepositAdminAudit(audit.Id);
        Assert.Multiple(() =>
        {
            AssertBoxState(updated, original);
            Assert.That(updated.Items.Select(item => item.EntityData), Is.EqualTo(new[] { "corrupt recovery YAML" }));
            Assert.That(receipt, Is.Not.Null);
            Assert.That(receipt.ItemData, Is.EqualTo("restorable item"));
            Assert.That(receipt.RoundId, Is.EqualTo(999), "Audit round IDs do not require an existing round.");
        });
    }

    [TestCase("yaml")]
    [TestCase("record-id")]
    [TestCase("item-count")]
    [TestCase("withdrawn-time")]
    [TestCase("withdrawn-round")]
    [TestCase("owner")]
    [TestCase("character-slot")]
    public async Task StaleSnapshotsCannotEraseConcurrentChangesOrCreateAudit(string change)
    {
        var expected = await AddBox(Guid.NewGuid(), 2, "first", "second");
        await using (var context = new SqliteServerDbContext(_options))
        {
            var changed = await context.WayfarerSafetyDepositBox.Include(box => box.Items)
                .SingleAsync(box => box.BoxId == expected.BoxId);
            switch (change)
            {
                case "yaml":
                    changed.Items[0].EntityData = "changed without changing record identity";
                    break;
                case "record-id":
                    var replaced = changed.Items[0];
                    context.WayfarerSafetyDepositBoxItem.Remove(replaced);
                    context.WayfarerSafetyDepositBoxItem.Add(new WayfarerSafetyDepositBoxItem
                    {
                        BoxId = changed.Id,
                        EntityData = replaced.EntityData,
                        DepositDate = DateTime.UtcNow,
                    });
                    break;
                case "item-count":
                    context.WayfarerSafetyDepositBoxItem.Remove(changed.Items[0]);
                    break;
                case "withdrawn-time":
                    changed.LastWithdrawn = DateTime.UtcNow;
                    break;
                case "withdrawn-round":
                    changed.LastWithdrawnRoundId = 18;
                    break;
                case "owner":
                    changed.OwnerUserId = Guid.NewGuid();
                    break;
                case "character-slot":
                    changed.CharacterIndex++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(change));
            }

            await context.SaveChangesAsync();
        }

        var concurrent = await _database.GetSafetyDepositBox(expected.BoxId);
        var audit = CreateAudit(expected);
        Assert.That(await _database.TryAdminReplaceSafetyDepositBoxItems(expected, [], audit), Is.False);

        var actual = await _database.GetSafetyDepositBox(expected.BoxId);
        Assert.Multiple(() =>
        {
            AssertBoxState(actual, concurrent);
            Assert.That(actual.Items.Select(item => (item.Id, item.EntityData)),
                Is.EquivalentTo(concurrent.Items.Select(item => (item.Id, item.EntityData))));
        });
        Assert.That(await _database.GetSafetyDepositAdminAudit(audit.Id), Is.Null);
        Assert.That(await _database.GetSafetyDepositAdminAudits(expected.BoxId), Is.Empty);
    }

    [Test]
    public async Task ReusingOperationIdCannotApplyAnotherMutationEvenWithAFreshSnapshot()
    {
        var original = await AddBox(Guid.NewGuid(), 1, "original");
        var operation = CreateAudit(original, "original");
        Assert.That(await _database.TryAdminReplaceSafetyDepositBoxItems(original, ["retained"], operation), Is.True);

        var current = await _database.GetSafetyDepositBox(original.BoxId);
        var replay = CreateAudit(current, "different recovery payload");
        replay.Id = operation.Id;
        Assert.That(await _database.TryAdminReplaceSafetyDepositBoxItems(current, ["duplicate"], replay), Is.False);

        var updated = await _database.GetSafetyDepositBox(original.BoxId);
        var receipt = await _database.GetSafetyDepositAdminAudit(operation.Id);
        Assert.That(updated.Items.Select(item => (item.Id, item.EntityData)),
            Is.EqualTo(current.Items.Select(item => (item.Id, item.EntityData))));
        Assert.That(receipt.ItemData, Is.EqualTo("original"));
        Assert.That(await _database.GetSafetyDepositAdminAudits(original.BoxId), Has.Count.EqualTo(1));
    }

    [TestCase("safety_deposit_admin_audit")]
    [TestCase("wayfarer_safety_deposit_box_item")]
    public async Task FailureWritingEitherAuditOrReplacementRollsBackTheEntireOperation(string failingTable)
    {
        var original = await AddBox(Guid.NewGuid(), 0, "must survive", "also retained");
        await using (var context = new SqliteServerDbContext(_options))
        {
            // Both table names are fixed NUnit test cases, never external input.
            await context.Database.ExecuteSqlRawAsync($"""
                CREATE TRIGGER fail_admin_write BEFORE INSERT ON {failingTable}
                BEGIN SELECT RAISE(ABORT, 'Injected administrative write failure'); END;
                """);
        }

        var audit = CreateAudit(original, "must survive");
        Assert.ThrowsAsync<DbUpdateException>(async () =>
            await _database.TryAdminReplaceSafetyDepositBoxItems(original, ["replacement"], audit));

        var unchanged = await _database.GetSafetyDepositBox(original.BoxId);
        Assert.Multiple(() =>
        {
            AssertBoxState(unchanged, original);
            Assert.That(unchanged.Items.Select(item => (item.Id, item.EntityData)),
                Is.EquivalentTo(original.Items.Select(item => (item.Id, item.EntityData))));
        });
        Assert.That(await _database.GetSafetyDepositAdminAudit(audit.Id), Is.Null);
        Assert.That(await _database.GetSafetyDepositAdminAudits(original.BoxId), Is.Empty);
    }

    [Test]
    public async Task CommittedAuditCanBeCompletedWithoutLosingRecoveryPayloadAndHistoryDoesNotReadIt()
    {
        var box = await AddBox(Guid.NewGuid(), 0, "item to remove");
        var audit = CreateAudit(box, new string('x', 256 * 1024));
        Assert.That(await _database.TryAdminReplaceSafetyDepositBoxItems(box, [], audit), Is.True);
        await _database.CompleteSafetyDepositAdminAudit(audit.Id, "rolled-back", "recipient disconnected");

        var receipt = await _database.GetSafetyDepositAdminAudit(audit.Id);
        Assert.Multiple(() =>
        {
            Assert.That(receipt.Result, Is.EqualTo("rolled-back"));
            Assert.That(receipt.Details, Is.EqualTo("recipient disconnected"));
            Assert.That(receipt.ItemData, Is.EqualTo(audit.ItemData));
            Assert.That(receipt.AdminUserId, Is.EqualTo(audit.AdminUserId));
            Assert.That(receipt.OwnerUserId, Is.EqualTo(audit.OwnerUserId));
        });

        _capture.Columns.Clear();
        var history = await _database.GetSafetyDepositAdminAudits(box.BoxId);
        Assert.Multiple(() =>
        {
            Assert.That(history, Has.Count.EqualTo(1));
            Assert.That(history[0].Id, Is.EqualTo(audit.Id));
            Assert.That(history[0].Result, Is.EqualTo("rolled-back"));
            Assert.That(history[0].ItemData, Is.Null);
            Assert.That(_capture.Columns, Is.Not.Empty);
            Assert.That(_capture.Columns.SelectMany(columns => columns), Does.Not.Contain("item_data"),
                "Audit history must not load recovery payloads only to discard them afterward.");
        });
    }

    [Test]
    public async Task AuditHistoryIsScopedNewestFirstAndBounded()
    {
        var box = await AddBox(Guid.NewGuid(), 0);
        var otherBox = await AddBox(Guid.NewGuid(), 0);
        var createdAt = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
        var operations = new List<SafetyDepositAdminAudit>();
        for (var index = 0; index < 205; index++)
        {
            var audit = CreateAudit(box, "private recovery data");
            audit.CreatedAt = createdAt.AddSeconds(index);
            operations.Add(audit);
        }

        var unrelated = CreateAudit(otherBox);
        unrelated.CreatedAt = createdAt.AddDays(1);
        await using (var context = new SqliteServerDbContext(_options))
        {
            context.SafetyDepositAdminAudits.AddRange(operations);
            context.SafetyDepositAdminAudits.Add(unrelated);
            await context.SaveChangesAsync();
        }

        var latest = await _database.GetSafetyDepositAdminAudits(box.BoxId, 2);
        Assert.That(latest.Select(audit => audit.Id), Is.EqualTo(new[] { operations[204].Id, operations[203].Id }));
        Assert.That(await _database.GetSafetyDepositAdminAudits(box.BoxId), Has.Count.EqualTo(50));
        Assert.That(await _database.GetSafetyDepositAdminAudits(box.BoxId, int.MaxValue), Has.Count.EqualTo(200));
    }

    [Test]
    public async Task DeletedBoxCannotBeRecreatedByStaleOperationAndItsExistingAuditSurvives()
    {
        var box = await AddBox(Guid.NewGuid(), 4, "lost box item");
        var previous = CreateAudit(box, "recoverable prior operation");
        previous.Result = "pending";
        await _database.AddSafetyDepositAdminAudit(previous);
        await _database.DeleteSafetyDepositBox(box.BoxId);

        var attempted = CreateAudit(box);
        Assert.That(await _database.TryAdminReplaceSafetyDepositBoxItems(box, ["must not reappear"], attempted), Is.False);
        Assert.That(await _database.GetSafetyDepositBox(box.BoxId), Is.Null);
        Assert.That(await _database.GetSafetyDepositAdminAudit(attempted.Id), Is.Null);
        var priorReceipt = await _database.GetSafetyDepositAdminAudit(previous.Id);
        Assert.That(priorReceipt.ItemData, Is.EqualTo("recoverable prior operation"));
        var resolution = CreateResolution(box, true);
        Assert.That(await _database.TryResolveSafetyDepositAdminWithdrawal(previous.Id, "pending", true, resolution), Is.False);
        Assert.That(await _database.GetSafetyDepositAdminAudit(resolution.Id), Is.Null);
        Assert.That((await _database.GetSafetyDepositAdminAudit(previous.Id)).Result, Is.EqualTo("pending"));
        await using var context = new SqliteServerDbContext(_options);
        Assert.That(await context.WayfarerSafetyDepositBoxItem.CountAsync(), Is.Zero);
    }

    [Test]
    public async Task PreparedRecoveryAppendsExactlyOnceWithoutOverwritingLaterBoxContentsOrStatus()
    {
        var original = await AddBox(Guid.NewGuid(), 3, "withdrawn item", "retained item");
        var withdrawal = CreateAudit(original, "withdrawn item");
        withdrawal.Action = "WithdrawStored";
        withdrawal.Result = "prepared";
        Assert.That(await _database.TryAdminReplaceSafetyDepositBoxItems(original, ["retained item"], withdrawal), Is.True);

        // An identical later item is legitimate; recovery must neither discard it nor deduplicate by YAML.
        await _database.SetSafetyDepositBoxWithdrawnItems(original.BoxId, 88, ["retained item", "withdrawn item"]);
        var current = await _database.GetSafetyDepositBox(original.BoxId);
        var resolution = CreateResolution(current, true);
        Assert.That(resolution.AdminUserId, Is.Not.EqualTo(withdrawal.AdminUserId), "Another administrator may resolve the operation.");
        Assert.That(await _database.TryResolveSafetyDepositAdminWithdrawal(withdrawal.Id, "prepared", true, resolution), Is.True);

        var recovered = await OpenDatabase().GetSafetyDepositBox(original.BoxId);
        Assert.Multiple(() =>
        {
            AssertBoxState(recovered, current);
            Assert.That(recovered.Items.Select(item => item.EntityData),
                Is.EquivalentTo(new[] { "retained item", "withdrawn item", "withdrawn item" }));
            Assert.That(recovered.Items.Where(item => current.Items.Any(previous => previous.Id == item.Id))
                .Select(item => (item.Id, item.EntityData)),
                Is.EquivalentTo(current.Items.Select(item => (item.Id, item.EntityData))), "Existing rows must retain their identities.");
        });
        var receipt = await _database.GetSafetyDepositAdminAudit(withdrawal.Id);
        Assert.That(receipt.Result, Is.EqualTo("recovered"));
        Assert.That(receipt.ItemData, Is.EqualTo("withdrawn item"));
        Assert.That(await _database.GetSafetyDepositAdminAudit(resolution.Id), Is.Not.Null);
        Assert.That(await _database.TryResolveSafetyDepositAdminWithdrawal(withdrawal.Id, "prepared", true, resolution), Is.False);
        Assert.That(await _database.TryResolveSafetyDepositAdminWithdrawal(withdrawal.Id, "prepared", true,
            CreateResolution(current, true)), Is.False);
        Assert.That(await _database.TryTransitionSafetyDepositAdminWithdrawal(withdrawal.Id, "prepared", "delivering"), Is.False);
        Assert.That(await _database.TryTransitionSafetyDepositAdminWithdrawal(withdrawal.Id, "delivering", "success"), Is.False);
        Assert.That((await _database.GetSafetyDepositBox(original.BoxId)).Items, Has.Count.EqualTo(3));
        Assert.That(await _database.GetSafetyDepositAdminAudits(original.BoxId), Has.Count.EqualTo(2));
        Assert.That(await _database.GetSafetyDepositAdminRecoveries(original.BoxId), Is.Empty);
    }

    [Test]
    public async Task DeliveryTransitionsCannotSkipPreparationReplayOrMutateLegacyWithdrawals()
    {
        var box = await AddBox(Guid.NewGuid(), 0);
        var operation = CreateAudit(box);
        operation.Action = "WithdrawStored";
        operation.Result = "prepared";
        await _database.AddSafetyDepositAdminAudit(operation);
        Assert.ThrowsAsync<ArgumentException>(async () =>
            await _database.TryTransitionSafetyDepositAdminWithdrawal(operation.Id, "prepared", "success"));
        Assert.That((await _database.GetSafetyDepositAdminAudit(operation.Id)).Result, Is.EqualTo("prepared"));
        Assert.That(await _database.TryTransitionSafetyDepositAdminWithdrawal(operation.Id, "prepared", "delivering"), Is.True);
        Assert.That(await _database.TryTransitionSafetyDepositAdminWithdrawal(operation.Id, "prepared", "delivering"), Is.False);
        Assert.That((await _database.GetSafetyDepositAdminAudit(operation.Id)).Result, Is.EqualTo("delivering"));
        Assert.That(await _database.TryTransitionSafetyDepositAdminWithdrawal(operation.Id, "delivering", "success"), Is.True);
        Assert.That(await _database.TryTransitionSafetyDepositAdminWithdrawal(operation.Id, "delivering", "success"), Is.False);
        Assert.That((await _database.GetSafetyDepositAdminAudit(operation.Id)).Result, Is.EqualTo("success"));

        var legacy = CreateAudit(box);
        legacy.Result = "prepared";
        await _database.AddSafetyDepositAdminAudit(legacy);
        Assert.That(await _database.TryTransitionSafetyDepositAdminWithdrawal(legacy.Id, "prepared", "delivering"), Is.False);
        Assert.That((await _database.GetSafetyDepositAdminAudit(legacy.Id)).Result, Is.EqualTo("prepared"));
    }

    [Test]
    public async Task StaleResolutionAndConfirmationBeforeDeliveryCannotModifyBoxOrCreateReceipts()
    {
        var box = await AddBox(Guid.NewGuid(), 0, "current content");
        var operation = CreateAudit(box, "withdrawn item");
        operation.Action = "WithdrawStored";
        operation.Result = "prepared";
        await _database.AddSafetyDepositAdminAudit(operation);
        var stale = CreateResolution(box, true);
        var premature = CreateResolution(box, false);
        Assert.That(await _database.TryResolveSafetyDepositAdminWithdrawal(operation.Id, "delivering", true, stale), Is.False);
        Assert.That(await _database.TryResolveSafetyDepositAdminWithdrawal(operation.Id, "prepared", false, premature), Is.False);
        Assert.That((await _database.GetSafetyDepositAdminAudit(operation.Id)).Result, Is.EqualTo("prepared"));
        Assert.That(await _database.GetSafetyDepositAdminAudit(stale.Id), Is.Null);
        Assert.That(await _database.GetSafetyDepositAdminAudit(premature.Id), Is.Null);

        Assert.That(await _database.TryTransitionSafetyDepositAdminWithdrawal(operation.Id, "prepared", "delivering"), Is.True);
        Assert.That(await _database.TryResolveSafetyDepositAdminWithdrawal(operation.Id, "prepared", true, stale), Is.False);
        var unchanged = await _database.GetSafetyDepositBox(box.BoxId);
        Assert.That(unchanged.Items.Select(item => (item.Id, item.EntityData)),
            Is.EqualTo(box.Items.Select(item => (item.Id, item.EntityData))));
        Assert.That((await _database.GetSafetyDepositAdminAudit(operation.Id)).Result, Is.EqualTo("delivering"));
        Assert.That(await _database.GetSafetyDepositAdminAudits(box.BoxId), Has.Count.EqualTo(1));
    }

    [TestCase("WithdrawStored", "delivering")]
    [TestCase("Withdraw", "pending")]
    public async Task ConfirmingUncertainDeliveryNeedsNeitherBoxNorPayloadAndCannotBeReplayed(string action, string state)
    {
        var box = await AddBox(Guid.NewGuid(), 4, "old box content");
        var operation = CreateAudit(box);
        operation.Action = action;
        operation.Result = state;
        operation.ItemData = null;
        await _database.AddSafetyDepositAdminAudit(operation);
        await _database.DeleteSafetyDepositBox(box.BoxId);
        var resolution = CreateResolution(box, false);
        Assert.That(await _database.TryResolveSafetyDepositAdminWithdrawal(operation.Id, state, false, resolution), Is.True);
        Assert.That((await _database.GetSafetyDepositAdminAudit(operation.Id)).Result, Is.EqualTo("confirmed"));
        Assert.That((await _database.GetSafetyDepositAdminAudit(resolution.Id)).Action, Is.EqualTo("ConfirmWithdrawal"));
        Assert.That(await _database.TryResolveSafetyDepositAdminWithdrawal(operation.Id, state, false,
            CreateResolution(box, false)), Is.False);
        Assert.That(await _database.TryTransitionSafetyDepositAdminWithdrawal(operation.Id, "delivering", "success"), Is.False);
        Assert.That(await _database.GetSafetyDepositBox(box.BoxId), Is.Null);
        Assert.That(await _database.GetSafetyDepositAdminRecoveries(box.BoxId), Is.Empty);
        Assert.That(await _database.GetSafetyDepositAdminAudits(box.BoxId), Has.Count.EqualTo(2));
    }

    [TestCase("safety_deposit_admin_audit")]
    [TestCase("wayfarer_safety_deposit_box_item")]
    public async Task FailedRecoveryRollsBackSourceReceiptAppendedItemAndResolutionReceiptTogether(string failingTable)
    {
        var box = await AddBox(Guid.NewGuid(), 0, "current content");
        var operation = CreateAudit(box, "withdrawn item");
        operation.Action = "WithdrawStored";
        operation.Result = "prepared";
        await _database.AddSafetyDepositAdminAudit(operation);
        await using (var context = new SqliteServerDbContext(_options))
        {
            // Fixed test-case table names inject a failure after the source receipt's CAS update.
            await context.Database.ExecuteSqlRawAsync($"""
                CREATE TRIGGER fail_recovery_write BEFORE INSERT ON {failingTable}
                BEGIN SELECT RAISE(ABORT, 'Injected recovery write failure'); END;
                """);
        }

        var resolution = CreateResolution(box, true);
        Assert.ThrowsAsync<DbUpdateException>(async () =>
            await _database.TryResolveSafetyDepositAdminWithdrawal(operation.Id, "prepared", true, resolution));
        var unchanged = await _database.GetSafetyDepositBox(box.BoxId);
        Assert.That(unchanged.Items.Select(item => (item.Id, item.EntityData)),
            Is.EqualTo(box.Items.Select(item => (item.Id, item.EntityData))));
        Assert.That((await _database.GetSafetyDepositAdminAudit(operation.Id)).Result, Is.EqualTo("prepared"));
        Assert.That(await _database.GetSafetyDepositAdminAudit(resolution.Id), Is.Null);
        Assert.That(await _database.GetSafetyDepositAdminRecoveries(box.BoxId), Has.Count.EqualTo(1));
    }

    [Test]
    public async Task RecoveryListKeepsOldUnfinishedOperationsOutsideHistoryWindowWithoutReadingPayloads()
    {
        var box = await AddBox(Guid.NewGuid(), 0);
        var otherBox = await AddBox(Guid.NewGuid(), 0);
        var prepared = CreateAudit(box);
        prepared.Action = "WithdrawStored";
        prepared.Result = "prepared";
        prepared.CreatedAt = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var delivering = CreateAudit(box);
        delivering.Action = "WithdrawStored";
        delivering.Result = "delivering";
        delivering.CreatedAt = prepared.CreatedAt;
        var legacyStored = CreateAudit(box);
        legacyStored.Result = "pending";
        legacyStored.Details = "source: database";
        legacyStored.CreatedAt = prepared.CreatedAt;
        var legacyWorld = CreateAudit(box);
        legacyWorld.Result = "pending";
        legacyWorld.Details = "source: world";
        legacyWorld.CreatedAt = prepared.CreatedAt;
        var unrelated = CreateAudit(otherBox);
        unrelated.Action = "WithdrawStored";
        unrelated.Result = "prepared";
        await using (var context = new SqliteServerDbContext(_options))
        {
            context.SafetyDepositAdminAudits.AddRange(prepared, delivering, legacyStored, legacyWorld, unrelated);
            for (var index = 0; index < 205; index++)
            {
                var finished = CreateAudit(box);
                finished.Action = "WithdrawStored";
                context.SafetyDepositAdminAudits.Add(finished);
            }
            await context.SaveChangesAsync();
        }

        Assert.That((await _database.GetSafetyDepositAdminAudits(box.BoxId)).Any(a => a.Id == prepared.Id), Is.False);
        _capture.Columns.Clear();
        var recoveries = await _database.GetSafetyDepositAdminRecoveries(box.BoxId);
        Assert.Multiple(() =>
        {
            Assert.That(recoveries.Select(a => a.Id),
                Is.EquivalentTo(new[] { prepared.Id, delivering.Id, legacyStored.Id, legacyWorld.Id }));
            Assert.That(recoveries.All(a => a.ItemData == null), Is.True);
            Assert.That(_capture.Columns, Is.Not.Empty);
            Assert.That(_capture.Columns.SelectMany(columns => columns), Does.Not.Contain("item_data"));
        });
    }

    [TestCase("Withdraw", "recovered")]
    [TestCase("Withdraw", "confirmed")]
    [TestCase("WithdrawStored", "prepared")]
    [TestCase("WithdrawStored", "delivering")]
    [TestCase("WithdrawStored", "success")]
    public async Task GenericCompletionCannotOverwriteStoredWithdrawalProtocolOrFinalResolutions(string action, string result)
    {
        var box = await AddBox(Guid.NewGuid(), 0);
        var operation = CreateAudit(box, "original recovery payload");
        operation.Action = action;
        operation.Result = result;
        await _database.AddSafetyDepositAdminAudit(operation);

        await _database.CompleteSafetyDepositAdminAudit(operation.Id, "success", "late physical completion");
        var unchanged = await _database.GetSafetyDepositAdminAudit(operation.Id);
        Assert.Multiple(() =>
        {
            Assert.That(unchanged.Result, Is.EqualTo(result));
            Assert.That(unchanged.Details, Is.EqualTo("Administrative recovery test"));
            Assert.That(unchanged.ItemData, Is.EqualTo("original recovery payload"));
        });
    }

    [Test]
    public async Task GenericCompletionStillUpdatesPhysicalReceiptsAndRejectsMissingOperations()
    {
        var box = await AddBox(Guid.NewGuid(), 0);
        var operation = CreateAudit(box, "physical item snapshot");
        operation.Result = "pending";
        await _database.AddSafetyDepositAdminAudit(operation);
        await _database.CompleteSafetyDepositAdminAudit(operation.Id, "success", "physical item delivered");
        var completed = await _database.GetSafetyDepositAdminAudit(operation.Id);
        Assert.Multiple(() =>
        {
            Assert.That(completed.Result, Is.EqualTo("success"));
            Assert.That(completed.Details, Is.EqualTo("physical item delivered"));
            Assert.That(completed.ItemData, Is.EqualTo("physical item snapshot"));
        });
        Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await _database.CompleteSafetyDepositAdminAudit(Guid.NewGuid(), "success", "missing receipt"));
    }

    private async Task<WayfarerSafetyDepositBox> AddBox(Guid ownerId, int characterIndex, params string[] itemData)
    {
        var box = new WayfarerSafetyDepositBox
        {
            BoxId = Guid.NewGuid(),
            OwnerUserId = ownerId,
            CharacterIndex = characterIndex,
            OwnerName = "Test owner",
            Nickname = "Preserve this label",
            ProtoId = "TestSafetyDepositBox",
            PurchaseDate = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
        };
        foreach (var data in itemData)
        {
            box.Items.Add(new WayfarerSafetyDepositBoxItem
            {
                EntityData = data,
                DepositDate = box.PurchaseDate,
            });
        }

        await using var context = new SqliteServerDbContext(_options);
        context.WayfarerSafetyDepositBox.Add(box);
        await context.SaveChangesAsync();
        return await _database.GetSafetyDepositBox(box.BoxId);
    }

    private static SafetyDepositAdminAudit CreateAudit(WayfarerSafetyDepositBox box, string itemData = "recovery data")
    {
        return new SafetyDepositAdminAudit
        {
            Id = Guid.NewGuid(),
            AdminUserId = Guid.NewGuid(),
            AdminName = "Test administrator",
            OwnerUserId = box.OwnerUserId,
            CharacterIndex = box.CharacterIndex,
            BoxId = box.BoxId,
            CreatedAt = DateTime.UtcNow,
            Action = "Withdraw",
            Result = "success",
            Details = "Administrative recovery test",
            ItemData = itemData,
            RoundId = 999,
        };
    }

    private static SafetyDepositAdminAudit CreateResolution(WayfarerSafetyDepositBox box, bool restore)
    {
        var resolution = CreateAudit(box);
        resolution.Action = restore ? "RestoreWithdrawal" : "ConfirmWithdrawal";
        resolution.ItemData = null;
        return resolution;
    }

    private static void AssertBoxState(WayfarerSafetyDepositBox actual, WayfarerSafetyDepositBox expected)
    {
        Assert.That(actual, Is.Not.Null);
        Assert.That(actual.Id, Is.EqualTo(expected.Id));
        Assert.That(actual.BoxId, Is.EqualTo(expected.BoxId));
        Assert.That(actual.OwnerUserId, Is.EqualTo(expected.OwnerUserId));
        Assert.That(actual.CharacterIndex, Is.EqualTo(expected.CharacterIndex));
        Assert.That(actual.OwnerName, Is.EqualTo(expected.OwnerName));
        Assert.That(actual.ProtoId, Is.EqualTo(expected.ProtoId));
        Assert.That(actual.Nickname, Is.EqualTo(expected.Nickname));
        Assert.That(actual.PurchaseDate, Is.EqualTo(expected.PurchaseDate));
        Assert.That(actual.LastWithdrawn, Is.EqualTo(expected.LastWithdrawn));
        Assert.That(actual.LastWithdrawnRoundId, Is.EqualTo(expected.LastWithdrawnRoundId));
    }

    private ServerDbSqlite OpenDatabase()
    {
        return new ServerDbSqlite(() => _options, true, _configuration, true,
            _logs.GetSawmill("safety-deposit-admin-test"));
    }

    private sealed class ReaderColumnCapture : DbCommandInterceptor
    {
        public readonly List<string[]> Columns = [];

        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData eventData,
            DbDataReader result, CancellationToken cancellationToken = default)
        {
            var columns = new string[result.FieldCount];
            for (var index = 0; index < columns.Length; index++)
                columns[index] = result.GetName(index);
            Columns.Add(columns);
            return ValueTask.FromResult(result);
        }
    }
}
