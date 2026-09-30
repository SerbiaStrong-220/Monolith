using System.Collections.Generic;
using System.Numerics;
using Content.Server._Exodus.Stealth;
using Content.Server.Storage.Components;
using Content.Server.Storage.EntitySystems;
using Content.Shared._Exodus.Stealth.Components;
using Content.Shared._Exodus.Stealth.Systems;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Timing;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(StealthDisruptorSystem))]
public sealed partial class StealthDisruptorTest
{
    [Test]
    public async Task PulseFindsNestedOccupantsAndBoxesAndRespectsRangeAndCooldown()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var otherMap = entities.System<SharedMapSystem>().CreateMap();
            var user = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var remote = entities.SpawnEntity("StealthDisruptor", new EntityCoordinates(map, Vector2.Zero));
            var near = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, 1, 0));
            var visible = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, 1, -1));
            var box = entities.SpawnEntity("StealthBox", new EntityCoordinates(map, 16, 0));
            var locker = entities.SpawnEntity("CrateGenericSteel", new EntityCoordinates(map, 6, 0));
            var nestedBox = entities.SpawnEntity("StealthBox", new EntityCoordinates(map, 6, 0));
            var nestedOccupant = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, 6, 0));
            var otherOccupant = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, 6, 0));
            var distant = entities.SpawnEntity("StealthBox", new EntityCoordinates(map, 16.1f, 0));
            var differentMap = entities.SpawnEntity("StealthBox", new EntityCoordinates(otherMap, 1, 0));
            entities.SpawnEntity("WallSolid", new EntityCoordinates(map, 2, 0));
            var containers = entities.System<SharedContainerSystem>();
            var inner = entities.GetComponent<EntityStorageComponent>(nestedBox).Contents;
            var outer = entities.GetComponent<EntityStorageComponent>(locker).Contents;
            Assert.That(containers.Insert(nestedOccupant, inner), Is.True);
            Assert.That(containers.Insert(nestedBox, outer), Is.True);
            Assert.That(containers.Insert(otherOccupant, outer), Is.True);
            var stealth = entities.System<SharedStealthSystem>();
            foreach (var target in new[] { near, nestedOccupant, otherOccupant })
                Assert.That(stealth.RequestStealth(target, "Test", new StealthData(lastVisibility: -1f)), Is.True);

            var use = new UseInHandEvent(user);
            entities.EventBus.RaiseLocalEvent(remote, use);
            Assert.That(use.Handled, Is.True);
            foreach (var target in new[] { near, box, nestedBox, nestedOccupant, otherOccupant })
            {
                Assert.That(stealth.IsSuppressed(target), Is.True);
                Assert.That(stealth.GetVisibility(target), Is.EqualTo(1f));
            }
            Assert.That(stealth.IsSuppressed(visible), Is.False);
            Assert.That(stealth.IsSuppressed(distant), Is.False, "Radius is measured to the target, not its collider edge.");
            Assert.That(stealth.IsSuppressed(differentMap), Is.False);
            Assert.That(entities.GetComponent<EntityStorageComponent>(locker).Open, Is.False);
            Assert.That(containers.Remove(nestedOccupant, inner), Is.True);
            Assert.That(stealth.IsSuppressed(nestedOccupant), Is.True, "Leaving a container must not clear suppression.");

            var markers = new List<EntityUid>();
            var effects = entities.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (effects.MoveNext(out _, out var metadata, out var transform))
            {
                if (metadata.EntityPrototype?.ID == "StealthRevealEffect")
                    markers.Add(transform.ParentUid);
            }
            Assert.That(markers, Is.EquivalentTo(new[] { near, box, locker }),
                "Nested targets share one effect on their outermost container.");

            var delay = entities.System<UseDelaySystem>();
            Assert.That(delay.TryGetDelayInfo(remote, out var info), Is.True);
            var available = info!.EndTime;
            Assert.That(available - info.StartTime, Is.EqualTo(TimeSpan.FromSeconds(60)));
            Assert.That(stealth.RequestStealth(visible, "LateTest", new StealthData(lastVisibility: -1f)), Is.True);
            use = new UseInHandEvent(user);
            entities.EventBus.RaiseLocalEvent(remote, use);
            Assert.That(use.Handled, Is.False);
            Assert.That(stealth.IsSuppressed(visible), Is.False);
            Assert.That(info.EndTime, Is.EqualTo(available), "A refused activation must not restart the cooldown.");
            entities.DeleteEntity(map);
            entities.DeleteEntity(otherMap);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SuppressionSurvivesBoxReclosureAndPreservesPassiveSources()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid map = default;
        EntityUid box = default;
        await server.WaitAssertion(() =>
        {
            map = entities.System<SharedMapSystem>().CreateMap();
            box = entities.SpawnEntity("StealthBox", new EntityCoordinates(map, Vector2.Zero));
            var stealth = entities.System<SharedStealthSystem>();
            Assert.That(stealth.RequestStealth(box, "Extra", new StealthData(lastVisibility: -1f)), Is.True);
            Assert.That(stealth.TrySuppress(box, TimeSpan.FromSeconds(60)), Is.True);
            Assert.That(stealth.RemoveRequest("Extra", box), Is.True);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var stealth = entities.System<SharedStealthSystem>();
            Assert.That(entities.HasComponent<StealthComponent>(box), Is.True,
                "Removing one layer during suppression must preserve other sources.");
            entities.System<EntityStorageSystem>().OpenStorage(box);
            Assert.That(stealth.IsSuppressed(box), Is.True);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            entities.System<EntityStorageSystem>().CloseStorage(box);
            var stealth = entities.System<SharedStealthSystem>();
            Assert.That(stealth.IsSuppressed(box), Is.True);
            Assert.That(stealth.GetVisibility(box), Is.EqualTo(1f));
            Assert.That(entities.HasComponent<StealthComponent>(box), Is.True);
            var suppressed = entities.GetComponent<StealthSuppressedComponent>(box);
            suppressed.Until = server.ResolveDependency<IGameTiming>().CurTime;
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var stealth = entities.System<SharedStealthSystem>();
            Assert.That(stealth.IsSuppressed(box), Is.False);
            Assert.That(stealth.IsVisible(box), Is.False, "The box's passive cloak resumes after the timed suppression.");
            Assert.That(stealth.GetVisibility(box), Is.GreaterThan(0f), "The box starts fading from visible again.");
            entities.DeleteEntity(map);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase(MobState.Critical)]
    [TestCase(MobState.Dead)]
    public async Task SuppressionPreservesMobStateCleanup(MobState state)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid map = default;
        EntityUid body = default;
        await server.WaitAssertion(() =>
        {
            map = entities.System<SharedMapSystem>().CreateMap();
            body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var stealth = entities.System<SharedStealthSystem>();
            Assert.That(stealth.RequestStealth(body, "Test",
                new StealthData(lastVisibility: -1f, enabledOnDeath: false, enabledOnCrit: false)), Is.True);
            Assert.That(stealth.TrySuppress(body, TimeSpan.FromSeconds(60)), Is.True);
            entities.System<MobStateSystem>().ChangeMobState(body, state);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<StealthComponent>(body), Is.False,
                "Suppression must not prevent a cloak's ordinary crit/death cleanup.");
            entities.DeleteEntity(map);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
