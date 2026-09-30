using System.Linq;
using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Server.Power.EntitySystems;
using Content.Shared.Damage.Components;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [TestCase("complete")]
    [TestCase("gene")]
    [TestCase("occupied-hand")]
    public async Task HulkPriesByHandWithoutReplacingNormalInteractions(string outcome)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var hands = entities.System<SharedHandsSystem>();
        EntityUid map = default;
        EntityUid body = default;
        EntityUid door = default;
        EntityUid item = default;
        string context = default!;
        Content.Shared.DoAfter.DoAfter operation = default!;

        await server.WaitAssertion(() =>
        {
            map = entities.System<SharedMapSystem>().CreateMap();
            body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            entities.EnsureComponent<GodmodeComponent>(body);
            var genome = Enable(entities, body, "GeneticHulk");
            context = genome.Context;
            var interaction = entities.System<SharedInteractionSystem>();
            var doors = entities.System<SharedDoorSystem>();

            var ordinaryDoor = entities.SpawnEntity("WoodDoor", new EntityCoordinates(map, new Vector2(0, 1)));
            Assert.That(doors.CanOpen(ordinaryDoor, user: body), Is.True);
            Assert.That(interaction.InteractHand(body, ordinaryDoor), Is.True);
            Assert.That(entities.GetComponent<DoorComponent>(ordinaryDoor).State, Is.EqualTo(DoorState.Opening));
            Assert.That(entities.GetComponent<DoAfterComponent>(body).DoAfters, Is.Empty,
                "Doors that can open normally must not start genetic prying.");
            entities.DeleteEntity(ordinaryDoor);

            item = entities.SpawnEntity("Crowbar", new EntityCoordinates(map, new Vector2(0.2f, 0)));
            Assert.That(interaction.InteractHand(body, item), Is.True);
            Assert.That(hands.TryGetActiveItem(body, out var held), Is.True);
            Assert.That(held, Is.EqualTo(item), "Hulk must not interfere with picking up items.");
            Assert.That(hands.TryDrop(body), Is.True);

            door = entities.SpawnEntity("Airlock", new EntityCoordinates(map, new Vector2(1, 0)));
            entities.System<PowerReceiverSystem>().SetNeedsPower(door, false);
        });

        await PoolManager.WaitUntil(server, () => entities.System<PowerReceiverSystem>().IsPowered(door));
        await server.WaitAssertion(() =>
        {
            var interaction = entities.System<SharedInteractionSystem>();
            var doors = entities.System<SharedDoorSystem>();
            var bolts = entities.GetComponent<DoorBoltComponent>(door);
            Assert.That(doors.TrySetBoltDown((door, bolts), true), Is.True);
            Assert.That(doors.CanOpen(door, user: body), Is.False);
            var state = entities.GetComponent<GeneticAbilityStateComponent>(body);
            doors.SetState(door, DoorState.Denying);
            interaction.InteractHand(body, door);
            Assert.That(entities.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Opening),
                "Hulk must pry immediately, even during an access-denied animation.");
            Assert.That(entities.GetComponent<DoAfterComponent>(body).DoAfters, Is.Empty);
            doors.SetState(door, DoorState.Closed);

            // Keep cancellation coverage for explicitly configured nonzero pry times.
            state.PryTime = server.ResolveDependency<IGameTiming>().TickPeriod * 4;
            interaction.InteractHand(body, door);
            var operations = entities.GetComponent<DoAfterComponent>(body).DoAfters;
            Assert.That(operations.Count, Is.EqualTo(1), "Clicking a blocked door must start prying without an action button.");
            operation = operations.Values.Single();
            Assert.That(operation.Args.NeedHand, Is.True);
            Assert.That(hands.TryGetActiveItem(body, out _), Is.False, "Prying must not put a tool in the hand.");
        });

        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(operation.Cancelled, Is.False);
            Assert.That(operation.Completed, Is.False);
            if (outcome == "gene")
                Assert.That(entities.System<GeneticsSystem>().TryStabilize(body), Is.True);
            else if (outcome == "occupied-hand")
                Assert.That(hands.TryPickup(body, item), Is.True);
        });

        await server.WaitRunTicks(5);
        await server.WaitAssertion(() =>
        {
            var doorState = entities.GetComponent<DoorComponent>(door).State;
            Assert.That(doorState == DoorState.Closed, Is.EqualTo(outcome != "complete"));
            if (outcome == "occupied-hand")
                Assert.That(operation.Cancelled, Is.True);
            entities.DeleteEntity(map);
            DeleteCipher(entities, context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
