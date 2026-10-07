using Content.Shared._Exodus.Chemistry;
using Content.Shared.Mind;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [TestCase("MobAsakimGhostrole")]
    [TestCase("MobCenturionAsakimGhostrole")]
    [TestCase("MobCenturionAsakimGhostroleNoTimelock")]
    [TestCase("MobPrefectAsakimGhostrole")]
    [TestCase("MobPrefectAsakimGhostroleNoTimelock")]
    public async Task GhostRoleNeedsStartOnlyOnFirstPossession(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid body = default;
        EntityUid control = default;
        EntityUid mind = default;
        var context = string.Empty;
        (float Food, float Water, TimeSpan Drug) waiting = default;
        (float Food, float Water, TimeSpan Drug) controlBefore = default;
        (float Food, float Water, TimeSpan Drug) occupied = default;
        var ticks = 0;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity(prototype, map.MapCoords);
            control = entities.SpawnEntity("MobAsakim", map.MapCoords);
            // Additional genetic drains must obey the same suspension as baseline hunger and thirst.
            context = Enable(entities, body, "GeneticHypermetabolism").Context;
            Enable(entities, body, "GeneticThirst");
            waiting = GetNeeds(body);
            controlBefore = GetNeeds(control);
            ticks = server.ResolveDependency<IGameTiming>().TickRate * 3;
        });

        await pair.RunTicksSync(ticks);
        await server.WaitAssertion(() =>
        {
            Assert.That(GetNeeds(body), Is.EqualTo(waiting), "An unclaimed role must retain all three reserves.");
            AssertConsumed(GetNeeds(control), controlBefore, consumeDrug: false);

            var minds = entities.System<SharedMindSystem>();
            mind = minds.CreateMind(pair.Player!.UserId).Owner;
            minds.TransferTo(mind, body);
            Assert.That(GetNeeds(body), Is.EqualTo(waiting), "First possession must not charge the time spent waiting.");
        });

        await pair.RunTicksSync(ticks);
        await server.WaitAssertion(() =>
        {
            occupied = GetNeeds(body);
            AssertConsumed(occupied, waiting);
            entities.System<SharedMindSystem>().TransferTo(mind, null, createGhost: false);
            Assert.That(GetNeeds(body), Is.EqualTo(occupied), "Leaving a body must not refill its reserves.");
        });

        await pair.RunTicksSync(ticks);
        await server.WaitAssertion(() =>
        {
            AssertConsumed(GetNeeds(body), occupied, consumeDrug: false);
            var beforeSecondPossession = GetNeeds(body);
            entities.System<SharedMindSystem>().TransferTo(mind, body);
            Assert.That(GetNeeds(body), Is.EqualTo(beforeSecondPossession), "Returning must not reset the reserves.");
            entities.System<SharedMindSystem>().TransferTo(mind, null, createGhost: false);
            Assert.That(GetNeeds(body), Is.EqualTo(beforeSecondPossession));

            entities.DeleteEntity(mind);
            entities.DeleteEntity(map.MapUid);
            DeleteCipher(entities, context);
        });
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        (float Food, float Water, TimeSpan Drug) GetNeeds(EntityUid uid)
        {
            return (entities.System<HungerSystem>().GetHunger(entities.GetComponent<HungerComponent>(uid)),
                entities.GetComponent<ThirstComponent>(uid).CurrentThirst,
                entities.GetComponent<ChemicalDependencyComponent>(uid).Reserve);
        }

        static void AssertConsumed((float Food, float Water, TimeSpan Drug) after,
            (float Food, float Water, TimeSpan Drug) before, bool consumeDrug = true)
        {
            Assert.Multiple(() =>
            {
                Assert.That(after.Food, Is.LessThan(before.Food));
                Assert.That(after.Water, Is.LessThan(before.Water));
                Assert.That(after.Drug, consumeDrug ? Is.LessThan(before.Drug) : Is.EqualTo(before.Drug));
            });
        }
    }
}
