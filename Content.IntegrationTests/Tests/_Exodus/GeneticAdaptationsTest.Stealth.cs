using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Shared._Exodus.Genetics;
using Content.Shared._Exodus.Stealth.Components;
using Content.Shared._Exodus.Stealth.Systems;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [Test]
    public async Task SuppressionBreaksGeneticAndEquippedCloaksAndBlocksReactivation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var coat = entities.SpawnEntity("ClothingOuterCoatDetective", new EntityCoordinates(map, Vector2.Zero));
            var cloak = entities.EnsureComponent<ActiveCloakComponent>(coat);
            var inventory = entities.System<InventorySystem>();
            Assert.That(inventory.TryEquip(body, coat, "outerClothing", silent: true, force: true), Is.True);
            var toggle = new ToggleActiveCloakEvent { Performer = body };
            entities.EventBus.RaiseLocalEvent(coat, toggle);
            Assert.That(cloak.Enabled, Is.True);
            var genome = Enable(entities, body, "GeneticCloak");
            var gene = new GeneticCloakEvent { Performer = body };
            entities.EventBus.RaiseLocalEvent(body, gene);
            Assert.That(gene.Handled, Is.True);
            var state = entities.GetComponent<GeneticAbilityStateComponent>(body);
            var stealth = entities.System<SharedStealthSystem>();
            Assert.That(stealth.RequestStealth(body, "PassiveTest", new StealthData(lastVisibility: -1f)), Is.True);
            Assert.That(stealth.TrySuppress(body, TimeSpan.FromSeconds(20)), Is.True);
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            Assert.That(state.Cloaked, Is.False);
            Assert.That(state.CloakAvailable, Is.EqualTo(now + state.CloakCooldown));
            Assert.That(cloak.Enabled, Is.False, "The reveal event must reach equipment through inventory relay.");
            Assert.That(cloak.BrokenTime, Is.EqualTo(now));
            Assert.That(entities.HasComponent<StealthComponent>(body), Is.True, "Other cloak layers must survive.");

            // Use a fresh cloak and reset the gene's cooldown to verify the independent suppression lock.
            state.CloakAvailable = TimeSpan.Zero;
            Assert.That(inventory.TryUnequip(body, "outerClothing", silent: true, force: true), Is.True);
            var freshCoat = entities.SpawnEntity("ClothingOuterCoatDetective", new EntityCoordinates(map, Vector2.Zero));
            var freshCloak = entities.EnsureComponent<ActiveCloakComponent>(freshCoat);
            Assert.That(inventory.TryEquip(body, freshCoat, "outerClothing", silent: true, force: true), Is.True);
            gene = new GeneticCloakEvent { Performer = body };
            entities.EventBus.RaiseLocalEvent(body, gene);
            toggle = new ToggleActiveCloakEvent { Performer = body };
            entities.EventBus.RaiseLocalEvent(freshCoat, toggle);
            Assert.That(state.Cloaked, Is.False);
            Assert.That(freshCloak.Enabled, Is.False);
            Assert.That(stealth.GetVisibility(body), Is.EqualTo(1f));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
