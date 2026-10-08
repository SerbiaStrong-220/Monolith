// (c) Space Exodus Team - EXDS-RL with CLA
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Pair;
using Content.Server._WF.SafetyDepositBox;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Server.Stack;
using Content.Shared._Exodus.SafetyDepositBox;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared.Administration;
using Content.Shared.Hands.Components;
using Content.Shared.Mind;
using Content.Shared.Players;
using Content.Shared.Stacks;
using Content.Shared.Storage;
using NUnit.Framework;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.UnitTesting;
using Robust.UnitTesting.Pool;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class SafetyDepositAdminHandTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: entity
          id: ExodusSafetyDepositHandStack
          parent: BaseItem
          name: hand transfer stack
          components:
          - type: Sprite
            sprite: Objects/Materials/Sheets/metal.rsi
            state: steel
          - type: Item
            size: Tiny
            shape:
            - 0,0,0,0
          - type: Stack
            stackType: Steel
            count: 1
          - type: Appearance

        - type: entity
          id: ExodusSafetyDepositHandRestricted
          parent: ExodusSafetyDepositHandStack
          components:
          - type: SafetyDepositRestricted

        - type: entity
          id: ExodusSafetyDepositHandOversized
          parent: ExodusSafetyDepositHandStack
          components:
          - type: Item
            size: Huge
            shape:
            - 0,0,0,0

        - type: entity
          id: ExodusSafetyDepositHandFiller
          parent: ExodusSafetyDepositHandStack
          components:
          - type: Item
            size: Small
            shape:
            - 0,0,1,1
        """;

    [Test]
    public async Task StoredHandTransferPreservesChangedStateAndRemovesTheCommittedOriginal()
    {
        await using var pair = await PoolManager.GetServerClient(Settings());
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        try
        {
            var body = await AttachAdmin(pair, map.GridCoords);
            var database = server.ResolveDependency<IServerDbManager>();
            var ownerId = pair.Player!.UserId.UserId;
            var box = await database.PurchaseSafetyDepositBox(ownerId, 3, "owner of test box", "SafetyDepositBoxSmall");
            await database.UpdateSafetyDepositBoxNickname(box.BoxId, "hand-test vault");
            var held = await HoldChangedStack(pair, body, map.GridCoords);
            await server.WaitAssertion(() =>
            {
                Assert.That(entities.GetComponent<HandsComponent>(body).ActiveHandEntity, Is.EqualTo(held));
                Assert.That(entities.EntityExists(held), Is.True, "The source must remain real until persistence succeeds.");
            });

            Assert.That(await AddFromHand(pair, ownerId, box.BoxId), Is.EqualTo("admin-safety-deposit-success"));
            var stored = await database.GetSafetyDepositBox(box.BoxId);
            Assert.Multiple(() =>
            {
                Assert.That(stored.Items, Has.Count.EqualTo(1));
                Assert.That(stored.OwnerUserId, Is.EqualTo(ownerId));
                Assert.That(stored.CharacterIndex, Is.EqualTo(3));
                Assert.That(stored.OwnerName, Is.EqualTo("owner of test box"));
                Assert.That(stored.ProtoId, Is.EqualTo("SafetyDepositBoxSmall"));
                Assert.That(stored.Nickname, Is.EqualTo("hand-test vault"));
                Assert.That(stored.LastWithdrawn, Is.Null);
                Assert.That(stored.LastWithdrawnRoundId, Is.Null);
                Assert.That(stored.PurchaseDate, Is.EqualTo(box.PurchaseDate));
            });

            var audits = await database.GetSafetyDepositAdminAudits(box.BoxId);
            Assert.That(audits, Has.Count.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(audits[0].Action, Is.EqualTo("AddFromHand"));
                Assert.That(audits[0].Result, Is.EqualTo("success"));
                Assert.That(audits[0].AdminUserId, Is.EqualTo(ownerId));
                Assert.That(audits[0].OwnerUserId, Is.EqualTo(ownerId));
                Assert.That(audits[0].CharacterIndex, Is.EqualTo(3));
            });

            await pair.RunTicksSync(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(entities.EntityExists(held), Is.False, "A committed stored transfer must not leave a second physical copy.");
                Assert.That(entities.GetComponent<HandsComponent>(body).ActiveHandEntity, Is.Null);
                using var reader = new StringReader(stored.Items[0].EntityData);
                Assert.That(server.System<MapLoaderSystem>().TryLoadEntity(reader, "stored hand-transfer test", out var restored), Is.True);
                Assert.That(restored, Is.Not.Null);
                var item = restored.Value.Owner;
                Assert.Multiple(() =>
                {
                    Assert.That(entities.GetComponent<MetaDataComponent>(item).EntityName, Is.EqualTo("Changed hand stack"));
                    Assert.That(entities.GetComponent<StackComponent>(item).Count, Is.EqualTo(17));
                    Assert.That(entities.GetComponent<StackComponent>(item).MaxCountOverride, Is.EqualTo(73));
                });
                entities.DeleteEntity(item);
            });
        }
        finally
        {
            await Cleanup(pair, map);
        }

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task IssuedHandTransferMovesTheSameEntityWithoutCreatingADatabaseCopy()
    {
        await using var pair = await PoolManager.GetServerClient(Settings());
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        try
        {
            var body = await AttachAdmin(pair, map.GridCoords);
            var database = server.ResolveDependency<IServerDbManager>();
            var ownerId = pair.Player!.UserId.UserId;
            var box = await database.PurchaseSafetyDepositBox(ownerId, 3, "owner of test box", "SafetyDepositBoxSmall");
            var physical = await IssueBox(pair, box, map.GridCoords);
            var before = await database.GetSafetyDepositBox(box.BoxId);
            var held = await HoldChangedStack(pair, body, map.GridCoords);

            Assert.That(await AddFromHand(pair, ownerId, box.BoxId), Is.EqualTo("admin-safety-deposit-success"));
            await pair.RunTicksSync(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(entities.EntityExists(held), Is.True);
                Assert.That(entities.GetComponent<HandsComponent>(body).ActiveHandEntity, Is.Null);
                Assert.That(entities.GetComponent<StorageComponent>(physical).Container.ContainedEntities,
                    Is.EqualTo(new[] { held }), "The actual held entity must move; spawning its prototype loses its state.");
                Assert.That(entities.GetComponent<MetaDataComponent>(held).EntityName, Is.EqualTo("Changed hand stack"));
                Assert.That(entities.GetComponent<StackComponent>(held).Count, Is.EqualTo(17));
                Assert.That(entities.GetComponent<StackComponent>(held).MaxCountOverride, Is.EqualTo(73));
            });

            var after = await database.GetSafetyDepositBox(box.BoxId);
            Assert.Multiple(() =>
            {
                Assert.That(after.Items, Is.Empty, "An issued box keeps its contents in the world.");
                Assert.That(after.LastWithdrawn, Is.EqualTo(before.LastWithdrawn));
                Assert.That(after.LastWithdrawnRoundId, Is.EqualTo(before.LastWithdrawnRoundId));
            });
            var audits = await database.GetSafetyDepositAdminAudits(box.BoxId);
            Assert.That(audits, Has.Count.EqualTo(1));
            Assert.That(audits[0].Action, Is.EqualTo("AddFromHand"));
            Assert.That(audits[0].Result, Is.EqualTo("success"));
        }
        finally
        {
            await Cleanup(pair, map);
        }

        await pair.CleanReturnAsync();
    }

    [TestCase("blacklist", false, "ExodusSafetyDepositHandRestricted")]
    [TestCase("size", true, "ExodusSafetyDepositHandOversized")]
    [TestCase("full", false, "ExodusSafetyDepositHandStack")]
    public async Task RejectedHandTransferLeavesTheItemInHandAndTheBoxUnchanged(string rejection, bool issued, string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(Settings());
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        try
        {
            var body = await AttachAdmin(pair, map.GridCoords);
            var database = server.ResolveDependency<IServerDbManager>();
            var ownerId = pair.Player!.UserId.UserId;
            var box = await database.PurchaseSafetyDepositBox(ownerId, 3, "owner of test box", "SafetyDepositBoxSmall");
            EntityUid? physical = null;
            if (issued)
                physical = await IssueBox(pair, box, map.GridCoords);
            if (rejection == "full")
            {
                string fillerData = null;
                await server.WaitAssertion(() =>
                {
                    // This four-cell item fills the real small box's 2x2 grid completely.
                    var filler = entities.SpawnEntity("ExodusSafetyDepositHandFiller", map.GridCoords);
                    using var writer = new StringWriter();
                    Assert.That(server.System<MapLoaderSystem>().TrySaveEntity(filler, writer), Is.True);
                    fillerData = writer.ToString();
                    entities.DeleteEntity(filler);
                });
                await database.DepositSafetyDepositBoxItems(box.BoxId, [fillerData]);
            }

            var before = await database.GetSafetyDepositBox(box.BoxId);
            var held = await HoldChangedStack(pair, body, map.GridCoords, prototype);
            var outcome = await AddFromHand(pair, ownerId, box.BoxId);
            Assert.That(outcome, Is.EqualTo("admin-safety-deposit-error-capacity"), $"The {rejection} restriction must reject transfer.");
            await pair.RunTicksSync(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(entities.EntityExists(held), Is.True);
                Assert.That(entities.GetComponent<HandsComponent>(body).ActiveHandEntity, Is.EqualTo(held));
                Assert.That(entities.GetComponent<MetaDataComponent>(held).EntityName, Is.EqualTo("Changed hand stack"));
                Assert.That(entities.GetComponent<StackComponent>(held).Count, Is.EqualTo(17));
                if (physical is { } physicalBox)
                    Assert.That(entities.GetComponent<StorageComponent>(physicalBox).Container.ContainedEntities, Is.Empty);
            });

            var after = await database.GetSafetyDepositBox(box.BoxId);
            Assert.Multiple(() =>
            {
                Assert.That(after.Items.Select(item => (item.Id, item.EntityData)),
                    Is.EqualTo(before.Items.Select(item => (item.Id, item.EntityData))), "Rejected transfer cannot rewrite or discard stored records.");
                Assert.That(after.OwnerUserId, Is.EqualTo(ownerId));
                Assert.That(after.CharacterIndex, Is.EqualTo(3));
                Assert.That(after.LastWithdrawn, Is.EqualTo(before.LastWithdrawn));
                Assert.That(after.LastWithdrawnRoundId, Is.EqualTo(before.LastWithdrawnRoundId));
            });
        }
        finally
        {
            await Cleanup(pair, map);
        }

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task PrototypeAdditionRequiresSpawnPermissionForStoredAndIssuedBoxes(bool issued)
    {
        await using var pair = await PoolManager.GetServerClient(Settings());
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        try
        {
            await AttachAdmin(pair, map.GridCoords);
            var database = server.ResolveDependency<IServerDbManager>();
            var ownerId = pair.Player!.UserId.UserId;
            var box = await database.PurchaseSafetyDepositBox(ownerId, 3, "owner of test box", "SafetyDepositBoxSmall");
            await database.UpdateSafetyDepositBoxNickname(box.BoxId, "permission-test vault");
            EntityUid? physical = null;
            if (issued)
                physical = await IssueBox(pair, box, map.GridCoords);
            var before = await database.GetSafetyDepositBox(box.BoxId);
            var request = new AdminSafetyDepositModifyMessage(box.BoxId, Guid.NewGuid(), AdminSafetyDepositAction.Add,
                0, null, "ExodusSafetyDepositHandStack", "prototype-add permission test");

            Assert.That(await ModifyBox(pair, ownerId, request), Is.EqualTo("admin-safety-deposit-error-permission"),
                "The Admin flag must not grant permission to create new objects.");
            var denied = await database.GetSafetyDepositBox(box.BoxId);
            Assert.Multiple(() =>
            {
                Assert.That(denied.Items, Is.Empty);
                Assert.That(denied.OwnerUserId, Is.EqualTo(ownerId));
                Assert.That(denied.CharacterIndex, Is.EqualTo(3));
                Assert.That(denied.OwnerName, Is.EqualTo("owner of test box"));
                Assert.That(denied.ProtoId, Is.EqualTo("SafetyDepositBoxSmall"));
                Assert.That(denied.Nickname, Is.EqualTo("permission-test vault"));
                Assert.That(denied.PurchaseDate, Is.EqualTo(before.PurchaseDate));
                Assert.That(denied.LastWithdrawn, Is.EqualTo(before.LastWithdrawn));
                Assert.That(denied.LastWithdrawnRoundId, Is.EqualTo(before.LastWithdrawnRoundId));
            });
            Assert.That(await database.GetSafetyDepositAdminAudits(box.BoxId), Is.Empty,
                "Rejected creation must not create a successful receipt or pending physical mutation.");
            await server.WaitAssertion(() =>
            {
                if (physical is { } physicalBox)
                    Assert.That(entities.GetComponent<StorageComponent>(physicalBox).Container.ContainedEntities, Is.Empty);
                server.ResolveDependency<IAdminManager>().GetAdminData(pair.Player!)!.Flags = AdminFlags.Admin | AdminFlags.Spawn;
            });

            // Reuse the exact request to show that the first rejection was authorization, not invalid input.
            Assert.That(await ModifyBox(pair, ownerId, request), Is.EqualTo("admin-safety-deposit-success"));
            var allowed = await database.GetSafetyDepositBox(box.BoxId);
            Assert.Multiple(() =>
            {
                Assert.That(allowed.LastWithdrawn, Is.EqualTo(before.LastWithdrawn));
                Assert.That(allowed.LastWithdrawnRoundId, Is.EqualTo(before.LastWithdrawnRoundId));
                Assert.That(allowed.Nickname, Is.EqualTo("permission-test vault"));
                Assert.That(allowed.Items, Has.Count.EqualTo(issued ? 0 : 1));
            });
            var audits = await database.GetSafetyDepositAdminAudits(box.BoxId);
            Assert.That(audits, Has.Count.EqualTo(1));
            Assert.That(audits[0].Action, Is.EqualTo("Add"));
            Assert.That(audits[0].Result, Is.EqualTo("success"));
            await server.WaitAssertion(() =>
            {
                EntityUid added;
                if (physical is { } physicalBox)
                {
                    var contents = entities.GetComponent<StorageComponent>(physicalBox).Container.ContainedEntities;
                    Assert.That(contents, Has.Count.EqualTo(1));
                    added = contents.Single();
                }
                else
                {
                    using var reader = new StringReader(allowed.Items[0].EntityData);
                    Assert.That(server.System<MapLoaderSystem>().TryLoadEntity(reader, "permitted prototype-add test", out var restored), Is.True);
                    Assert.That(restored, Is.Not.Null);
                    added = restored.Value.Owner;
                }

                Assert.That(entities.GetComponent<MetaDataComponent>(added).EntityPrototype!.ID, Is.EqualTo("ExodusSafetyDepositHandStack"));
                Assert.That(entities.GetComponent<StackComponent>(added).Count, Is.EqualTo(1));
                if (!issued)
                    entities.DeleteEntity(added);
            });
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

    private static async Task<EntityUid> AttachAdmin(TestPair pair, EntityCoordinates coordinates)
    {
        var body = default(EntityUid);
        await pair.Server.WaitAssertion(() =>
        {
            var player = pair.Player!;
            var admins = pair.Server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(player);
            admins.GetAdminData(player)!.Flags = AdminFlags.Admin;
            pair.Server.System<SharedMindSystem>().WipeMind(player.ContentData()?.Mind);
            body = pair.Server.EntMan.SpawnEntity("MobHuman", coordinates);
            pair.Server.PlayerMan.SetAttachedEntity(player, body);
            Assert.That(player.AttachedEntity, Is.EqualTo(body));
        });
        return body;
    }

    private static async Task<EntityUid> HoldChangedStack(TestPair pair, EntityUid body, EntityCoordinates coordinates,
        string prototype = "ExodusSafetyDepositHandStack")
    {
        var item = default(EntityUid);
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            item = entities.SpawnEntity(prototype, coordinates);
            entities.GetComponent<StackComponent>(item).MaxCountOverride = 73;
            pair.Server.System<StackSystem>().SetCount(item, 17);
            pair.Server.System<MetaDataSystem>().SetEntityName(item, "Changed hand stack");
            var hands = entities.GetComponent<HandsComponent>(body);
            Assert.That(pair.Server.System<HandsSystem>().TryPickup(body, item, hands.ActiveHand!), Is.True);
            Assert.That(hands.ActiveHandEntity, Is.EqualTo(item));
        });
        return item;
    }

    private static async Task<EntityUid> IssueBox(TestPair pair, WayfarerSafetyDepositBox box, EntityCoordinates coordinates)
    {
        var roundId = 0;
        var physical = default(EntityUid);
        await pair.Server.WaitAssertion(() =>
        {
            roundId = pair.Server.System<GameTicker>().RoundId;
            physical = pair.Server.EntMan.SpawnEntity("SafetyDepositBoxSmall", coordinates);
            var component = pair.Server.EntMan.GetComponent<SafetyDepositBoxComponent>(physical);
            component.BoxId = box.BoxId;
            component.OwnerId = box.OwnerUserId;
            component.CharacterIndex = box.CharacterIndex;
            component.OwnerName = "owner of test box";
            pair.Server.EntMan.Dirty(physical, component);
        });
        await pair.Server.ResolveDependency<IServerDbManager>().SetSafetyDepositBoxWithdrawnItems(box.BoxId, roundId, []);
        return physical;
    }

    private static async Task<string> AddFromHand(TestPair pair, Guid ownerId, Guid boxId)
    {
        var request = new AdminSafetyDepositModifyMessage(boxId, Guid.NewGuid(), AdminSafetyDepositAction.AddFromHand,
            0, null, string.Empty, "hand-transfer integration test");
        return await ModifyBox(pair, ownerId, request);
    }

    private static async Task<string> ModifyBox(TestPair pair, Guid ownerId, AdminSafetyDepositModifyMessage request)
    {
        Task<string> operation = null;
        await pair.Server.WaitPost(() =>
        {
            operation = pair.Server.System<SafetyDepositBoxSystem>().AdminModifyBoxAsync(pair.Player!, ownerId, request, () => true);
        });
        await PoolManager.WaitUntil(pair.Server, () => operation.IsCompleted);
        return await operation;
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
}
