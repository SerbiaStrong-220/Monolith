// (c) Space Exodus Team - EXDS-RL with CLA
using System.IO;
using Content.IntegrationTests.Pair;
using Content.Server._Exodus.SafetyDepositBox;
using Content.Server._WF.SafetyDepositBox;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.Database._Exodus.SafetyDepositBox;
using Content.Server.GameTicking;
using Content.Server.Stack;
using Content.Shared._Exodus.SafetyDepositBox;
using Content.Shared.Administration;
using Content.Shared.Hands.Components;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Stacks;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class SafetyDepositAdminRecoveryTest
{
    [Test]
    public async Task StoredWithdrawalDeliversTheSavedItemAndFinalizesItsTwoPhaseReceipt()
    {
        await using var pair = await PoolManager.GetServerClient(Settings());
        var map = await pair.CreateTestMap();
        try
        {
            await AttachAdmin(pair, map);
            var database = pair.Server.ResolveDependency<IServerDbManager>();
            var prepared = await PrepareWithdrawal(pair, map, "WithdrawStored");
            Assert.That(await Resolve(pair, prepared, restore: true), Is.EqualTo("admin-safety-deposit-recovery-restored"));
            var restoredBox = await database.GetSafetyDepositBox(prepared.BoxId);
            Assert.That(restoredBox.Items, Has.Count.EqualTo(1));
            var request = new AdminSafetyDepositModifyMessage(prepared.BoxId, Guid.NewGuid(),
                AdminSafetyDepositAction.Withdraw, restoredBox.Items[0].Id, null, string.Empty,
                "Normal stored withdrawal integration test");
            Task<string> task = null;
            await pair.Server.WaitPost(() =>
            {
                task = pair.Server.System<SafetyDepositBoxSystem>().AdminModifyBoxAsync(
                    pair.Player!, prepared.OwnerUserId, request, () => true);
            });
            await PoolManager.WaitUntil(pair.Server, () => task.IsCompleted);
            Assert.That(await task, Is.EqualTo("admin-safety-deposit-success"));
            await AssertBoxUnlocked(pair, prepared.BoxId);
            Assert.That((await database.GetSafetyDepositBox(prepared.BoxId)).Items, Is.Empty);
            Assert.That(await database.GetSafetyDepositAdminRecoveries(prepared.BoxId), Is.Empty);
            var history = await database.GetSafetyDepositAdminAudits(prepared.BoxId);
            Assert.That(history, Has.Count.EqualTo(3));
            var withdrawal = history.Find(entry => entry.Id != prepared.Id && entry.Action == "WithdrawStored");
            Assert.That(withdrawal, Is.Not.Null);
            Assert.That(withdrawal!.Result, Is.EqualTo("success"));
            Assert.That((await database.GetSafetyDepositAdminAudit(withdrawal.Id))!.ItemData, Is.EqualTo(prepared.ItemData));
            await pair.RunTicksSync(1);
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.EntMan;
                Assert.That(pair.Player!.AttachedEntity, Is.Not.Null);
                var body = pair.Player.AttachedEntity!.Value;
                var held = entities.GetComponent<HandsComponent>(body).ActiveHandEntity;
                Assert.That(held, Is.Not.Null, "A fresh admin body has a free hand for the restored item.");
                Assert.Multiple(() =>
                {
                    Assert.That(entities.GetComponent<MetaDataComponent>(held!.Value).EntityName, Is.EqualTo("Original recovery stack"));
                    Assert.That(entities.GetComponent<StackComponent>(held.Value).Count, Is.EqualTo(17));
                    Assert.That(entities.GetComponent<TransformComponent>(held.Value).MapID,
                        Is.EqualTo(entities.GetComponent<TransformComponent>(body).MapID));
                    Assert.That(pair.Server.ResolveDependency<IAdminManager>().GetAdminData(pair.Player)!.Flags,
                        Is.EqualTo(AdminFlags.Admin), "Normal withdrawal must not require Spawn permission.");
                });
            });
        }
        finally
        {
            await Cleanup(pair, map);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PreparedWithdrawalWithoutLivingStagingRestoresItsOriginalOnceAndKeepsLaterDeposits()
    {
        await using var pair = await PoolManager.GetServerClient(Settings());
        var map = await pair.CreateTestMap();
        try
        {
            await AttachAdmin(pair, map);
            var database = pair.Server.ResolveDependency<IServerDbManager>();
            var operation = await PrepareWithdrawal(pair, map, "WithdrawStored");
            var box = await database.GetSafetyDepositBox(operation.BoxId);
            Assert.That(box.Items, Is.Empty, "The prepared withdrawal already consumed its stored row.");

            // Recovery must append to current contents instead of replacing the old whole-box snapshot.
            await database.DepositSafetyDepositBoxItems(operation.BoxId, [operation.ItemData!]);
            var laterDeposit = await database.GetSafetyDepositBox(operation.BoxId);
            var laterRecord = laterDeposit.Items[0].Id;

            Assert.That(await Resolve(pair, operation, restore: true), Is.EqualTo("admin-safety-deposit-recovery-restored"));
            await AssertBoxUnlocked(pair, operation.BoxId);
            var recovered = await database.GetSafetyDepositBox(operation.BoxId);
            Assert.Multiple(() =>
            {
                Assert.That(recovered.Items, Has.Count.EqualTo(2));
                Assert.That(recovered.Items.Exists(item => item.Id == laterRecord && item.EntityData == operation.ItemData), Is.True);
                Assert.That(recovered.Items.TrueForAll(item => item.EntityData == operation.ItemData), Is.True);
                Assert.That(recovered.OwnerUserId, Is.EqualTo(operation.OwnerUserId));
                Assert.That(recovered.CharacterIndex, Is.EqualTo(operation.CharacterIndex));
                Assert.That(recovered.Nickname, Is.EqualTo("recovery-test vault"));
                Assert.That(recovered.LastWithdrawn, Is.Null);
                Assert.That(recovered.LastWithdrawnRoundId, Is.Null);
            });
            Assert.That(await database.GetSafetyDepositAdminRecoveries(operation.BoxId), Is.Empty);
            var receipt = await database.GetSafetyDepositAdminAudit(operation.Id);
            Assert.That(receipt!.Result, Is.EqualTo("recovered"));
            var history = await database.GetSafetyDepositAdminAudits(operation.BoxId);
            Assert.That(history, Has.Count.EqualTo(2));
            Assert.That(history.Exists(entry => entry.Action == "RestoreWithdrawal" && entry.Result == "success" &&
                entry.AdminUserId == pair.Player!.UserId.UserId), Is.True);

            Assert.That(await Resolve(pair, operation, restore: true),
                Is.EqualTo("admin-safety-deposit-error-recovery-unavailable"));
            await AssertBoxUnlocked(pair, operation.BoxId);
            Assert.That((await database.GetSafetyDepositBox(operation.BoxId)).Items, Has.Count.EqualTo(2),
                "A repeated recovery request must never append the original again.");
            Assert.That(await database.GetSafetyDepositAdminAudits(operation.BoxId), Has.Count.EqualTo(2));
        }
        finally
        {
            await Cleanup(pair, map);
        }

        await pair.CleanReturnAsync();
    }

    [TestCase("WithdrawStored")]
    [TestCase("Withdraw")]
    public async Task UncertainWithdrawalRequiresSpawnForRestorationButAdminCanConfirmWithoutACopy(string action)
    {
        await using var pair = await PoolManager.GetServerClient(Settings());
        var map = await pair.CreateTestMap();
        try
        {
            await AttachAdmin(pair, map);
            var database = pair.Server.ResolveDependency<IServerDbManager>();
            var operation = await PrepareWithdrawal(pair, map, action);
            var uncertainResult = action == "WithdrawStored" ? "delivering" : "pending";
            if (action == "WithdrawStored")
                Assert.That(await database.TryTransitionSafetyDepositAdminWithdrawal(operation.Id, "prepared", "delivering"), Is.True);

            Assert.That(await Resolve(pair, operation, restore: true), Is.EqualTo("admin-safety-deposit-error-permission"));
            await AssertBoxUnlocked(pair, operation.BoxId);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(pair.Server.ResolveDependency<IAdminManager>().GetAdminData(pair.Player!)!.Flags,
                    Is.EqualTo(AdminFlags.Admin), "This scenario intentionally has no Spawn permission.");
            });
            Assert.That((await database.GetSafetyDepositBox(operation.BoxId)).Items, Is.Empty);
            Assert.That((await database.GetSafetyDepositAdminAudit(operation.Id))!.Result, Is.EqualTo(uncertainResult));
            Assert.That(await database.GetSafetyDepositAdminRecoveries(operation.BoxId), Has.Count.EqualTo(1));
            Assert.That(await database.GetSafetyDepositAdminAudits(operation.BoxId), Has.Count.EqualTo(1));

            Assert.That(await Resolve(pair, operation, restore: false), Is.EqualTo("admin-safety-deposit-recovery-confirmed"));
            await AssertBoxUnlocked(pair, operation.BoxId);
            Assert.That((await database.GetSafetyDepositBox(operation.BoxId)).Items, Is.Empty,
                "Confirming an uncertain delivery acknowledges its outcome without creating another stored copy.");
            Assert.That((await database.GetSafetyDepositAdminAudit(operation.Id))!.Result, Is.EqualTo("confirmed"));
            Assert.That(await database.GetSafetyDepositAdminRecoveries(operation.BoxId), Is.Empty);
            var history = await database.GetSafetyDepositAdminAudits(operation.BoxId);
            Assert.That(history, Has.Count.EqualTo(2));
            Assert.That(history.Exists(entry => entry.Action == "ConfirmWithdrawal" && entry.Result == "success" &&
                entry.AdminUserId == pair.Player!.UserId.UserId), Is.True);
        }
        finally
        {
            await Cleanup(pair, map);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SpawnPermissionAllowsExplicitUncertainRestorationExactlyOnce()
    {
        await using var pair = await PoolManager.GetServerClient(Settings());
        var map = await pair.CreateTestMap();
        try
        {
            await AttachAdmin(pair, map);
            var database = pair.Server.ResolveDependency<IServerDbManager>();
            var operation = await PrepareWithdrawal(pair, map, "WithdrawStored");
            Assert.That(await database.TryTransitionSafetyDepositAdminWithdrawal(operation.Id, "prepared", "delivering"), Is.True);
            await pair.Server.WaitAssertion(() =>
            {
                pair.Server.ResolveDependency<IAdminManager>().GetAdminData(pair.Player!)!.Flags =
                    AdminFlags.Admin | AdminFlags.Spawn;
            });

            Assert.That(await Resolve(pair, operation, restore: true), Is.EqualTo("admin-safety-deposit-recovery-restored"));
            await AssertBoxUnlocked(pair, operation.BoxId);
            var restored = await database.GetSafetyDepositBox(operation.BoxId);
            Assert.That(restored.Items, Has.Count.EqualTo(1));
            Assert.That(restored.Items[0].EntityData, Is.EqualTo(operation.ItemData));
            Assert.That((await database.GetSafetyDepositAdminAudit(operation.Id))!.Result, Is.EqualTo("recovered"));
            Assert.That(await database.GetSafetyDepositAdminRecoveries(operation.BoxId), Is.Empty);
            Assert.That(await Resolve(pair, operation, restore: true), Is.EqualTo("admin-safety-deposit-error-recovery-unavailable"));
            await AssertBoxUnlocked(pair, operation.BoxId);
            Assert.That((await database.GetSafetyDepositBox(operation.BoxId)).Items, Has.Count.EqualTo(1));
            Assert.That(await database.GetSafetyDepositAdminAudits(operation.BoxId), Has.Count.EqualTo(2));
        }
        finally
        {
            await Cleanup(pair, map);
        }

        await pair.CleanReturnAsync();
    }

    private static PoolSettings Settings()
    {
        return new PoolSettings
        {
            Connected = true,
            Fresh = true,
            Destructive = true,
            AdminLogsEnabled = true,
            DummyTicker = false,
        };
    }

    private static async Task AttachAdmin(TestPair pair, TestMapData map)
    {
        await pair.Server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var admins = pair.Server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(player);
            admins.GetAdminData(player)!.Flags = AdminFlags.Admin;
            pair.Server.System<SharedMindSystem>().WipeMind(player.ContentData()?.Mind);
            var body = pair.Server.EntMan.SpawnEntity("MobHuman", map.GridCoords);
            pair.Server.PlayerMan.SetAttachedEntity(player, body);
            Assert.That(player.AttachedEntity, Is.EqualTo(body));
        });
    }

    private static async Task<SafetyDepositAdminAudit> PrepareWithdrawal(TestPair pair, TestMapData map, string action)
    {
        var database = pair.Server.ResolveDependency<IServerDbManager>();
        var owner = pair.Player!.UserId.UserId;
        var box = await database.PurchaseSafetyDepositBox(owner, 3, "recovery box owner", "SafetyDepositBoxSmall");
        await database.UpdateSafetyDepositBoxNickname(box.BoxId, "recovery-test vault");
        string data = null;
        var original = default(EntityUid);
        var round = 0;
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            original = entities.SpawnEntity("ExodusSafetyDepositHandStack", map.GridCoords);
            pair.Server.System<MetaDataSystem>().SetEntityName(original, "Original recovery stack");
            pair.Server.System<StackSystem>().SetCount(original, 17);
            using var writer = new StringWriter();
            Assert.That(pair.Server.System<MapLoaderSystem>().TrySaveEntity(original, writer), Is.True);
            data = writer.ToString();
            Assert.That(SafetyDepositItemMetadata.TryRead(data, out _, out var name, out var count), Is.True);
            Assert.That(name, Is.EqualTo("Original recovery stack"));
            Assert.That(count, Is.EqualTo(17));
            round = pair.Server.System<GameTicker>().RoundId;
            entities.DeleteEntity(original);
            Assert.That(entities.EntityExists(original), Is.False,
                "Recovery must work without an in-memory source or retained staging entity.");
        });
        await database.DepositSafetyDepositBoxItems(box.BoxId, [data]);
        box = await database.GetSafetyDepositBox(box.BoxId);
        var operation = new SafetyDepositAdminAudit
        {
            Id = Guid.NewGuid(),
            AdminUserId = owner,
            AdminName = pair.Player!.Name,
            OwnerUserId = owner,
            CharacterIndex = box.CharacterIndex,
            BoxId = box.BoxId,
            CreatedAt = DateTime.UtcNow,
            Action = action,
            Result = action == "WithdrawStored" ? "prepared" : "pending",
            Details = "Source: database; withdrawal awaiting recovery integration test.",
            ItemData = data,
            RoundId = round,
        };
        Assert.That(await database.TryAdminReplaceSafetyDepositBoxItems(box, [], operation), Is.True);
        return operation;
    }

    private static async Task<string> Resolve(TestPair pair, SafetyDepositAdminAudit operation, bool restore)
    {
        Task<string> task = null;
        await pair.Server.WaitPost(() =>
        {
            var request = new AdminSafetyDepositResolveRecoveryMessage(operation.BoxId, Guid.NewGuid(),
                operation.Id, restore, "Explicit recovery integration test decision");
            task = pair.Server.System<SafetyDepositBoxSystem>().AdminResolveWithdrawalAsync(
                pair.Player!, operation.OwnerUserId, request, () => true);
        });
        await PoolManager.WaitUntil(pair.Server, () => task.IsCompleted);
        return await task;
    }

    private static async Task Cleanup(TestPair pair, TestMapData map)
    {
        await pair.Server.WaitPost(() =>
        {
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            if (pair.Server.EntMan.EntityExists(map.MapUid))
                pair.Server.EntMan.DeleteEntity(map.MapUid);
        });
        await pair.RunTicksSync(1);
    }

    private static async Task AssertBoxUnlocked(TestPair pair, Guid boxId)
    {
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(pair.Server.System<SafetyDepositBoxSystem>().IsAdminBoxBusy(boxId), Is.False,
                "A completed, rejected, or replayed recovery must not leave the box permanently busy.");
        });
    }
}
