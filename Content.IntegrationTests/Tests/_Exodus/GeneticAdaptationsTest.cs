using System.Numerics;
using Content.Server._Exodus.Genetics;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Actions;
using Content.Shared.Electrocution;
using Content.Shared.Flash;
using Content.Shared.Flash.Components;
using Content.Shared.Inventory;
using Content.Shared.NightVision;
using Content.Shared.Overlays;
using Content.Shared.StatusEffect;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(GeneticsSystem))]
public sealed partial class GeneticAdaptationsTest
{
    [Test]
    public async Task PhysiologyChangesStopAfterResetAndPreserveOtherInsulation()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var genetics = entities.System<GeneticsSystem>();
            var genome = Enable(entities, body, "GeneticHemophilia");
            Enable(entities, body, "GeneticClotting");
            Enable(entities, body, "GeneticInsulation");
            var blood = entities.GetComponent<BloodstreamComponent>(body);
            var bloodstream = entities.System<BloodstreamSystem>();
            Assert.That(bloodstream.TryModifyBleedAmount(body, 1), Is.True);
            Assert.That(blood.BleedAmount, Is.EqualTo(2f));
            genome.NextUpdate = TimeSpan.Zero;
            genetics.Update(0);
            Assert.That(blood.BleedAmount, Is.EqualTo(1.25f).Within(0.001f));

            var insulation = entities.AddComponent<InsulatedComponent>(body);
            entities.System<SharedElectrocutionSystem>().SetInsulatedSiemensCoefficient(body, 0.4f, insulation);
            var attempt = new ElectrocutionAttemptEvent(body, null, 1f, SlotFlags.NONE);
            entities.EventBus.RaiseLocalEvent(body, attempt);
            Assert.That(attempt.SiemensCoefficient, Is.Zero);

            Assert.That(genetics.TryStabilize(body), Is.True);
            Assert.That(bloodstream.TryModifyBleedAmount(body, 1), Is.True);
            Assert.That(blood.BleedAmount, Is.EqualTo(2.25f).Within(0.001f));
            genome.NextUpdate = TimeSpan.Zero;
            genetics.Update(0);
            Assert.That(blood.BleedAmount, Is.EqualTo(2.25f).Within(0.001f));
            attempt = new ElectrocutionAttemptEvent(body, null, 1f, SlotFlags.NONE);
            entities.EventBus.RaiseLocalEvent(body, attempt);
            Assert.That(attempt.SiemensCoefficient, Is.EqualTo(0.4f));
            Assert.That(entities.GetComponent<InsulatedComponent>(body), Is.SameAs(insulation));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PhotophobiaExtendsFlashesButDoesNotBypassEyeProtection()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
            var genome = Enable(entities, body, "GeneticPhotophobia");
            var flash = entities.System<SharedFlashSystem>();
            var statuses = entities.System<StatusEffectsSystem>();
            flash.Flash(body, null, null, TimeSpan.FromSeconds(2), 1f, displayPopup: false);
            Assert.That(statuses.TryGetTime(body, "Flashed", out var time), Is.True);
            Assert.That(time!.Value.Item2 - time.Value.Item1, Is.EqualTo(TimeSpan.FromSeconds(3.5)));
            Assert.That(statuses.TryRemoveStatusEffect(body, "Flashed"), Is.True);

            entities.AddComponent<FlashImmunityComponent>(body);
            flash.Flash(body, null, null, TimeSpan.FromSeconds(2), 1f, displayPopup: false);
            Assert.That(statuses.HasStatusEffect(body, "Flashed"), Is.False);
            entities.RemoveComponent<FlashImmunityComponent>(body);
            Assert.That(entities.System<GeneticsSystem>().TryStabilize(body), Is.True);
            flash.Flash(body, null, null, TimeSpan.FromSeconds(2), 1f, displayPopup: false);
            Assert.That(statuses.TryGetTime(body, "Flashed", out time), Is.True);
            Assert.That(time!.Value.Item2 - time.Value.Item1, Is.EqualTo(TimeSpan.FromSeconds(2)));
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    [TestCase("MobHuman", 2, false)]
    [TestCase("MobHuman", 2, true)]
    [TestCase("MobArachnid", 4, false)]
    [TestCase("MobArachnid", 4, true)]
    public async Task GeneticPocketAddsOneSlotAndDropsItsItemOnRemoval(string prototype, int nativePockets, bool removeEffects)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var body = entities.SpawnEntity(prototype, new EntityCoordinates(map, Vector2.Zero));
            var inventory = entities.System<InventorySystem>();
            Assert.That(CountPockets(inventory, body), Is.EqualTo(nativePockets));
            var nativeItem = entities.SpawnEntity("Screwdriver", new EntityCoordinates(map, Vector2.Zero));
            Assert.That(inventory.TryEquip(body, nativeItem, "pocket1", force: true), Is.True);
            Assert.That(inventory.TryGetSlotContainer(body, "pocket1", out var nativePocket, out _), Is.True);
            var nativeVision = entities.AddComponent<NightVisionComponent>(body);
            entities.System<SharedNightVisionSystem>().SetEnabled((body, nativeVision), true);
            var nativeLight = entities.AddComponent<PointLightComponent>(body);
            var genome = Enable(entities, body, "GeneticPouch");
            Enable(entities, body, "GeneticNightVision");
            Enable(entities, body, "GeneticGlow");

            Assert.That(CountPockets(inventory, body), Is.EqualTo(nativePockets + 1));
            Assert.That(inventory.TryGetSlotContainer(body, "geneticPocket", out var pocket, out var definition), Is.True);
            Assert.That(definition!.DependsOn, Is.Null, "A biological pocket must not require a jumpsuit.");
            Assert.That(definition.SlotGroup, Is.EqualTo("MainHotbar"));
            Enable(entities, body, "GeneticPouch");
            Assert.That(CountPockets(inventory, body), Is.EqualTo(nativePockets + 1), "Reconciliation must not duplicate slots.");
            var state = entities.GetComponent<GeneticAbilityStateComponent>(body);
            var item = entities.SpawnEntity("Screwdriver", new EntityCoordinates(map, Vector2.Zero));
            Assert.That(inventory.TryEquip(body, item, "geneticPocket"), Is.True);
            Assert.That(pocket!.ContainedEntity, Is.EqualTo(item));

            var vision = new GeneticNightVisionEvent
            {
                Performer = body,
                Action = Action(entities, genome, "ActionGeneticNightVision"),
            };
            entities.EventBus.RaiseLocalEvent(body, vision);
            var glow = new GeneticGlowEvent
            {
                Performer = body,
                Action = Action(entities, genome, "ActionGeneticGlow"),
            };
            entities.EventBus.RaiseLocalEvent(body, glow);
            Assert.That(vision.Handled && glow.Handled, Is.True);
            Assert.That(state.Glow, Is.Not.Null);

            if (removeEffects)
                entities.RemoveComponent<GeneticEffectsComponent>(body);
            else
                Assert.That(entities.System<GeneticsSystem>().TryStabilize(body), Is.True);
            Assert.That(CountPockets(inventory, body), Is.EqualTo(nativePockets));
            Assert.That(inventory.HasSlot(body, "geneticPocket"), Is.False);
            Assert.That(state.Glow, Is.Null);
            Assert.That(entities.EntityExists(item), Is.True);
            Assert.That(entities.System<SharedContainerSystem>().IsEntityInContainer(item), Is.False);
            Assert.That(entities.GetComponent<TransformComponent>(item).MapUid, Is.EqualTo(map));
            Assert.That(entities.GetComponent<NightVisionComponent>(body), Is.SameAs(nativeVision));
            Assert.That(nativeVision.Enabled, Is.True);
            Assert.That(entities.GetComponent<PointLightComponent>(body), Is.SameAs(nativeLight));
            Assert.That(inventory.TryGetSlotContainer(body, "pocket1", out var preservedPocket, out _), Is.True);
            Assert.That(preservedPocket, Is.SameAs(nativePocket));
            Assert.That(preservedPocket!.ContainedEntity, Is.EqualTo(nativeItem));
            if (!removeEffects)
            {
                Enable(entities, body, "GeneticPouch");
                Assert.That(CountPockets(inventory, body), Is.EqualTo(nativePockets + 1));
                Assert.That(inventory.TryGetSlotContainer(body, "geneticPocket", out var restoredPocket, out _), Is.True);
                Assert.That(restoredPocket!.ContainedEntity, Is.Null);
                Assert.That(inventory.TryEquip(body, item, "geneticPocket"), Is.True);
            }
            entities.DeleteEntity(map);
            DeleteCipher(entities, genome.Context);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }

    private static int CountPockets(InventorySystem inventory, EntityUid body)
    {
        var slots = inventory.GetSlotEnumerator(body, SlotFlags.POCKET);
        var count = 0;
        while (slots.MoveNext(out _))
            count++;
        return count;
    }

    private static GenomeComponent Enable(IEntityManager entities, EntityUid body, ProtoId<GeneticMutationPrototype> mutation)
    {
        var genetics = entities.System<GeneticsSystem>();
        Assert.That(genetics.TryGetLivingGenome(body, out var genome), Is.True);
        var block = genetics.GetRound().Mutations.IndexOf(mutation);
        Assert.That(block, Is.GreaterThanOrEqualTo(0));
        Assert.That(genetics.TrySetBlock((body, genome!), block, GeneticsSystem.MaxBlockValue, body), Is.True);
        return genome!;
    }

    private static Entity<BaseActionComponent> Action(IEntityManager entities, GenomeComponent genome, EntProtoId id)
    {
        var uid = genome.Actions[id]!.Value;
        return (uid, entities.GetComponent<InstantActionComponent>(uid));
    }

    private static void DeleteCipher(IEntityManager entities, string context)
    {
        var query = entities.AllEntityQueryEnumerator<GeneticsRoundComponent>();
        while (query.MoveNext(out var uid, out var round))
        {
            if (round.Context == context)
                entities.QueueDeleteEntity(uid);
        }
    }
}
