using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Server._Exodus.Medical;
using Content.Shared._Exodus.Genetics;
using Content.Shared._Exodus.Medical;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Spawners;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [TestCase("MobMoth", "complete")]
    [TestCase("MobMoth", "cancel")]
    [TestCase("MobMoth", "gene")]
    [TestCase("MobMoth", "resources")]
    [TestCase("MobReptilian", "complete")]
    public async Task CocoonWeavingChargesOnlyOnCompletionAndAllowsMultipleCocoons(string species, string outcome)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid body = default;
        EntityUid firstCocoon = default;
        string context = default!;
        await server.WaitAssertion(() =>
        {
            entities.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, Vector2i.Zero, map.Tile.Tile);
            body = entities.SpawnEntity(species, new EntityCoordinates(map.Grid, .5f, .5f));
            entities.EnsureComponent<GodmodeComponent>(body);
            var genome = species == "MobMoth"
                ? entities.GetComponent<GenomeComponent>(body)
                : Enable(entities, body, "GeneticCocoon");
            context = genome.Context;
            Assert.That(genome.Active.Select(id => id.Id), Does.Contain("GeneticCocoon"));
            Assert.That(genome.Actions.ContainsKey("ActionGeneticCocoon"), Is.True);
            var hunger = entities.System<HungerSystem>();
            hunger.SetHunger(body, 200);
            var thirst = entities.GetComponent<ThirstComponent>(body);
            // A modest water reserve must suffice without overhydrating any species.
            entities.System<ThirstSystem>().SetThirst(body, thirst, 250);
            var owner = entities.EnsureComponent<GeneticCocoonOwnerComponent>(body);
            Assert.That(owner.WeaveTime, Is.EqualTo(TimeSpan.FromSeconds(10)));
            owner.WeaveTime = server.ResolveDependency<IGameTiming>().TickPeriod * 4;

            var weave = new GeneticCocoonEvent { Performer = body };
            entities.EventBus.RaiseLocalEvent(body, weave);
            Assert.That(weave.Handled, Is.True);
            var operation = entities.GetComponent<DoAfterComponent>(body).DoAfters.Values.Single();
            Assert.That(operation.Args.Delay, Is.EqualTo(owner.WeaveTime));
            Assert.That(operation.Args.MultiplyDelay, Is.False);
            Assert.That(GetHealingCocoonsOnGrid(entities, map.Grid), Is.Empty);
            Assert.That(hunger.GetHunger(entities.GetComponent<HungerComponent>(body)), Is.EqualTo(200));
            Assert.That(thirst.CurrentThirst, Is.EqualTo(250));
            weave = new GeneticCocoonEvent { Performer = body };
            entities.EventBus.RaiseLocalEvent(body, weave);
            Assert.That(weave.Handled, Is.False, "Repeated clicks must not start concurrent weaving.");
            Assert.That(operation.Cancelled, Is.False);

            switch (outcome)
            {
                case "cancel":
                    entities.System<SharedDoAfterSystem>().Cancel(operation.Id);
                    break;
                case "gene":
                    Assert.That(entities.System<GeneticsSystem>().TryStabilize(body), Is.True);
                    break;
                case "resources":
                    entities.System<ThirstSystem>().SetThirst(body, thirst, 100);
                    break;
            }
        });
        await server.WaitRunTicks(6);
        await server.WaitAssertion(() =>
        {
            var cocoons = GetHealingCocoonsOnGrid(entities, map.Grid);
            var food = entities.System<HungerSystem>().GetHunger(entities.GetComponent<HungerComponent>(body));
            var water = entities.GetComponent<ThirstComponent>(body).CurrentThirst;
            if (outcome == "complete")
            {
                Assert.That(cocoons, Has.Count.EqualTo(1));
                Assert.That(food, Is.EqualTo(140).Within(0.2f));
                Assert.That(water, Is.EqualTo(190).Within(0.2f));
                firstCocoon = cocoons[0];
                Assert.That(entities.HasComponent<TimedDespawnComponent>(firstCocoon), Is.False);

                // Start another cocoon immediately while the first one still exists.
                var thirst = entities.GetComponent<ThirstComponent>(body);
                entities.System<ThirstSystem>().SetThirst(body, thirst, 250);
                entities.System<SharedMapSystem>().SetTile(map.Grid, map.Grid.Comp, new Vector2i(1, 0), map.Tile.Tile);
                entities.System<SharedTransformSystem>().SetCoordinates(body, new EntityCoordinates(map.Grid, 1.5f, .5f));
                var weave = new GeneticCocoonEvent { Performer = body };
                entities.EventBus.RaiseLocalEvent(body, weave);
                Assert.That(weave.Handled, Is.True, "An existing cocoon must not prevent weaving another.");
            }
            else
            {
                Assert.That(cocoons, Is.Empty);
                Assert.That(food, Is.EqualTo(200).Within(0.2f));
                Assert.That(water, Is.EqualTo(outcome == "resources" ? 100 : 250).Within(0.2f));
            }
        });
        await server.WaitRunTicks(6);
        await server.WaitAssertion(() =>
        {
            var cocoons = GetHealingCocoonsOnGrid(entities, map.Grid);
            Assert.That(cocoons, Has.Count.EqualTo(outcome == "complete" ? 2 : 0));
            if (outcome == "complete")
            {
                Assert.That(cocoons, Does.Contain(firstCocoon));
                Assert.That(entities.System<HungerSystem>().GetHunger(entities.GetComponent<HungerComponent>(body)),
                    Is.EqualTo(80).Within(0.2f));
                Assert.That(entities.GetComponent<ThirstComponent>(body).CurrentThirst, Is.EqualTo(190).Within(0.2f));
            }
            foreach (var cocoon in cocoons)
            {
                Assert.That(entities.HasComponent<TimedDespawnComponent>(cocoon), Is.False);
                entities.DeleteEntity(cocoon);
            }
            entities.DeleteEntity(body);
            DeleteCipher(entities, context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task CocoonTreatsCriticalPatientsAndStopsTreatmentOnExit()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var position = new EntityCoordinates(map, Vector2.Zero);
            var cocoon = entities.SpawnEntity("GeneticHealingCocoon", position);
            var body = entities.SpawnEntity("MobHuman", position);
            var other = entities.SpawnEntity("MobHuman", position);
            var item = entities.SpawnEntity("Wrench", position);
            var containers = entities.System<SharedContainerSystem>();
            Assert.That(containers.TryGetContainer(cocoon, "patient", out var container), Is.True);
            Assert.That(containers.Insert(item, container!), Is.False);
            Assert.That(containers.Insert(body, container!), Is.True);
            Assert.That(containers.Insert(other, container!), Is.False, "Only one patient may enter.");
            var damage = entities.GetComponent<DamageableComponent>(body);
            var damages = entities.System<DamageableSystem>();
            damages.SetDamage(body, damage, new DamageSpecifier
            {
                DamageDict = { ["Asphyxiation"] = 60, ["Blunt"] = 10, ["Heat"] = 10 },
            });
            entities.System<MobStateSystem>().ChangeMobState(body, MobState.Critical);
            var respiration = new RespirationAttemptEvent();
            entities.EventBus.RaiseLocalEvent(body, ref respiration);
            Assert.That(respiration.Cancelled, Is.True, "Critical patients must not accumulate more suffocation.");
            var patient = entities.GetComponent<CocoonPatientComponent>(body);
            patient.NextHeal = server.ResolveDependency<IGameTiming>().CurTime;
            entities.System<HealingCocoonSystem>().Update(0);
            Assert.That(damage.Damage.DamageDict["Asphyxiation"], Is.EqualTo(FixedPoint2.New(55)));
            Assert.That(damage.Damage.DamageDict["Blunt"], Is.LessThan(FixedPoint2.New(10)));
            Assert.That(damage.Damage.DamageDict["Heat"], Is.LessThan(FixedPoint2.New(10)));
            var healed = damage.TotalDamage;
            entities.System<HealingCocoonSystem>().Update(0);
            Assert.That(damage.TotalDamage, Is.EqualTo(healed), "Treatment must run once per interval.");

            Assert.That(containers.Remove(body, container!), Is.True);
            respiration = new RespirationAttemptEvent();
            entities.EventBus.RaiseLocalEvent(body, ref respiration);
            Assert.That(respiration.Cancelled, Is.False, "Respiratory support ends immediately on exit.");
            patient.NextHeal = server.ResolveDependency<IGameTiming>().CurTime;
            entities.System<HealingCocoonSystem>().Update(0);
            Assert.That(damage.TotalDamage, Is.EqualTo(healed));

            Assert.That(entities.IsQueuedForDeletion(cocoon), Is.True, "Leaving consumes the cocoon.");
            Assert.That(containers.Insert(body, container!), Is.False, "A ruptured cocoon cannot be reused.");
            var nextCocoon = entities.SpawnEntity("GeneticHealingCocoon", position);
            Assert.That(containers.TryGetContainer(nextCocoon, "patient", out var nextContainer), Is.True);
            Assert.That(containers.Insert(body, nextContainer!), Is.True);
            entities.System<MobStateSystem>().ChangeMobState(body, MobState.Dead);
            entities.GetComponent<CocoonPatientComponent>(body).NextHeal = server.ResolveDependency<IGameTiming>().CurTime;
            entities.System<HealingCocoonSystem>().Update(0);
            Assert.That(damage.TotalDamage, Is.EqualTo(healed));
            Assert.That(entities.System<MobStateSystem>().IsDead(body), Is.True);
            entities.DeleteEntity(map);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase("exit")]
    [TestCase("eject")]
    [TestCase("damage")]
    [TestCase("damage-twice")]
    [TestCase("delete")]
    public async Task CocoonRemovalAlwaysReleasesItsPatient(string removal)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid map = default;
        EntityUid cocoon = default;
        EntityUid body = default;
        EntityUid? broken = null;
        await server.WaitAssertion(() =>
        {
            map = entities.System<SharedMapSystem>().CreateMap();
            var position = new EntityCoordinates(map, Vector2.Zero);
            cocoon = entities.SpawnEntity("GeneticHealingCocoon", position);
            body = entities.SpawnEntity("MobHuman", position);
            entities.EnsureComponent<GodmodeComponent>(body);
            var containers = entities.System<SharedContainerSystem>();
            Assert.That(containers.TryGetContainer(cocoon, "patient", out var container), Is.True);
            Assert.That(containers.Insert(body, container!), Is.True);
            switch (removal)
            {
                case "exit":
                    Assert.That(containers.Remove(body, container!), Is.True);
                    break;
                case "eject":
                    containers.EmptyContainer(container!);
                    break;
                case "damage":
                case "damage-twice":
                    var damage = entities.GetComponent<DamageableComponent>(cocoon);
                    var damages = entities.System<DamageableSystem>();
                    damages.SetDamage(cocoon, damage, new DamageSpecifier { DamageDict = { ["Structural"] = 149 } });
                    Assert.That(entities.IsQueuedForDeletion(cocoon), Is.False);
                    damages.SetDamage(cocoon, damage, new DamageSpecifier { DamageDict = { ["Structural"] = 150 } });
                    if (removal == "damage-twice")
                        entities.EventBus.RaiseLocalEvent(cocoon, new DestructionEventArgs());
                    break;
                case "delete":
                    entities.DeleteEntity(cocoon);
                    break;
            }
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(cocoon), Is.True);
            Assert.That(entities.Deleted(body), Is.False);
            Assert.That(entities.System<SharedContainerSystem>().IsEntityInContainer(body), Is.False);
            Assert.That(entities.GetComponent<TransformComponent>(body).MapUid, Is.EqualTo(map));
            Assert.That(entities.HasComponent<CocoonPatientComponent>(body), Is.False);

            var remainsCount = 0;
            var query = entities.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var metadata, out var transform))
            {
                if (metadata.EntityPrototype?.ID != "GeneticHealingCocoonBroken" || transform.MapUid != map)
                    continue;
                remainsCount++;
                broken = uid;
            }

            Assert.That(remainsCount, Is.EqualTo(removal == "delete" ? 0 : 1),
                "Rupture leaves exactly one shell, even if destruction is requested twice in the same tick.");
            if (broken is { } shell)
            {
                Assert.That(entities.HasComponent<HealingCocoonComponent>(shell), Is.False);
                Assert.That(entities.System<SharedContainerSystem>().TryGetContainer(shell, "patient", out _), Is.False);
                var damage = entities.GetComponent<DamageableComponent>(shell);
                var damages = entities.System<DamageableSystem>();
                damages.SetDamage(shell, damage, new DamageSpecifier { DamageDict = { ["Structural"] = 29 } });
                Assert.That(entities.IsQueuedForDeletion(shell), Is.False);
                damages.SetDamage(shell, damage, new DamageSpecifier { DamageDict = { ["Structural"] = 30 } });
            }
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            if (broken is { } shell)
                Assert.That(entities.Deleted(shell), Is.True, "The torn shell must be destructible too.");
            entities.DeleteEntity(map);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    private static List<EntityUid> GetHealingCocoonsOnGrid(IEntityManager entities, EntityUid grid)
    {
        var cocoons = new List<EntityUid>();
        var query = entities.AllEntityQueryEnumerator<HealingCocoonComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out _, out var transform))
        {
            if (transform.GridUid == grid)
                cocoons.Add(uid);
        }
        return cocoons;
    }
}
