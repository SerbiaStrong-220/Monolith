using System.Collections.Generic;
using System.Numerics;
using Content.Shared._Exodus.Stealth.Components;
using Content.Shared._Exodus.Stealth.Systems;
using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.Polymorph.Components;
using Content.Shared.Polymorph.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class StealthDisruptorTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PulseRevealsProjectorDisguisesAndTemporarilyBlocksReuse(bool alsoCloaked)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid map = default;
        EntityUid wearer = default;
        EntityUid projector = default;
        EntityUid sample = default;
        await server.WaitAssertion(() =>
        {
            map = entities.System<SharedMapSystem>().CreateMap();
            var origin = new EntityCoordinates(map, Vector2.Zero);
            var user = entities.SpawnEntity("MobHuman", origin);
            var remote = entities.SpawnEntity("StealthDisruptor", origin);
            wearer = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, 16, 0));
            projector = entities.SpawnEntity("ChameleonProjector", new EntityCoordinates(map, 16, 0));
            sample = entities.SpawnEntity("Wrench", new EntityCoordinates(map, 15, 0));
            var distant = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, 16.1f, 0));
            var distantProjector = entities.SpawnEntity("ChameleonProjector", new EntityCoordinates(map, 16.1f, 0));
            var visible = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, 1, 0));
            var hands = entities.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(wearer, projector), Is.True);
            Assert.That(hands.TryPickupAnyHand(distant, distantProjector), Is.True);

            var chameleon = entities.System<SharedChameleonProjectorSystem>();
            Assert.That(chameleon.TryDisguise((projector, entities.GetComponent<ChameleonProjectorComponent>(projector)), wearer, sample), Is.True);
            Assert.That(chameleon.TryDisguise((distantProjector, entities.GetComponent<ChameleonProjectorComponent>(distantProjector)), distant, sample), Is.True);
            var disguise = entities.GetComponent<ChameleonDisguisedComponent>(wearer).Disguise;
            var stealth = entities.System<SharedStealthSystem>();
            Assert.That(entities.HasComponent<StealthComponent>(wearer), Is.False,
                "The projector must be detectable without a conventional stealth layer.");
            if (alsoCloaked)
                Assert.That(stealth.RequestStealth(wearer, "Test", new StealthData(lastVisibility: -1f)), Is.True);

            var use = new UseInHandEvent(user);
            entities.EventBus.RaiseLocalEvent(remote, use);
            Assert.That(use.Handled, Is.True);
            Assert.That(entities.HasComponent<ChameleonDisguisedComponent>(wearer), Is.False);
            Assert.That(entities.Deleted(disguise), Is.True, "The fake object's sprite must be removed.");
            Assert.That(entities.GetComponent<ChameleonProjectorComponent>(projector).Disguised, Is.Null);
            Assert.That(stealth.IsSuppressed(wearer), Is.True);
            Assert.That(stealth.GetVisibility(wearer), Is.EqualTo(1f));
            Assert.That(entities.GetComponent<StealthSuppressedComponent>(wearer).Until - server.ResolveDependency<IGameTiming>().CurTime,
                Is.EqualTo(TimeSpan.FromSeconds(20)));
            Assert.That(entities.HasComponent<ChameleonDisguisedComponent>(distant), Is.True);
            Assert.That(stealth.IsSuppressed(distant), Is.False, "A disguise outside the radius must remain intact.");
            Assert.That(stealth.IsSuppressed(visible), Is.False);
            foreach (var (_, action) in entities.System<SharedActionsSystem>().GetActions(wearer))
                Assert.That(action.Container, Is.Not.EqualTo(projector), "Disguise controls must be removed on reveal.");

            var markers = new List<EntityUid>();
            var effects = entities.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            while (effects.MoveNext(out _, out var metadata, out var transform))
            {
                if (metadata.EntityPrototype?.ID == "StealthRevealEffect")
                    markers.Add(transform.ParentUid);
            }
            Assert.That(markers, Is.EquivalentTo(new[] { wearer }),
                "The wearer gets one detection effect, even when also cloaked.");
            Assert.That(chameleon.TryDisguise((projector, entities.GetComponent<ChameleonProjectorComponent>(projector)), wearer, sample), Is.False);
            var replacement = entities.SpawnEntity("ChameleonProjector", new EntityCoordinates(map, 16, 0));
            Assert.That(chameleon.TryDisguise((replacement, entities.GetComponent<ChameleonProjectorComponent>(replacement)), wearer, sample), Is.False,
                "Switching projectors must not bypass suppression.");
            entities.GetComponent<StealthSuppressedComponent>(wearer).Until = server.ResolveDependency<IGameTiming>().CurTime;
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.System<SharedStealthSystem>().IsSuppressed(wearer), Is.False);
            Assert.That(entities.System<SharedChameleonProjectorSystem>().TryDisguise(
                (projector, entities.GetComponent<ChameleonProjectorComponent>(projector)), wearer, sample), Is.True,
                "The projector becomes usable again when suppression expires.");
            entities.DeleteEntity(map);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
