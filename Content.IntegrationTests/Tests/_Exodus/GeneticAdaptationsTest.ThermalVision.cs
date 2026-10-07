#nullable enable
using Content.Client._White.Overlays;
using Content.Server._Exodus.Genetics;
using Content.Shared._White.Overlays;
using Content.Shared.Actions;
using Content.Shared.Inventory;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task GeneticThermalPulsesSynchronizeAndStopWithoutRemovingHelmetVision(bool stabilize)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var (server, client) = pair;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid body = default;
        EntityUid helmet = default;
        EntityUid actionUid = default;
        NetEntity netBody = default;
        var context = string.Empty;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.MapCoords);
            helmet = entities.SpawnEntity("ClothingHelmetHardsuitAsakim", map.MapCoords);
            netBody = entities.GetNetEntity(body);
            Assert.That(entities.System<InventorySystem>().TryEquip(body, helmet, "head", silent: true, force: true), Is.True);
            server.PlayerMan.SetAttachedEntity(pair.Player!, body);
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.HasComponent<ThermalVisionComponent>(client.EntMan.GetEntity(netBody)), Is.False);
            Assert.That(client.ResolveDependency<IOverlayManager>().HasOverlay<ThermalVisionOverlay>(), Is.False);
        });

        await server.WaitAssertion(() =>
        {
            var genome = Enable(entities, body, "GeneticThermalVision");
            context = genome.Context;
            Assert.That(genome.Stability, Is.EqualTo(30));
            var thermal = entities.GetComponent<ThermalVisionComponent>(body);
            var helmetThermal = entities.GetComponent<ThermalVisionComponent>(helmet);
            Assert.That(thermal.IsEquipment, Is.False);
            Assert.That(thermal.PulseTime, Is.EqualTo(helmetThermal.PulseTime).And.EqualTo(3f));
            Assert.That(thermal.ToggleActionEntity, Is.Not.Null);
            actionUid = thermal.ToggleActionEntity!.Value;
            var action = entities.GetComponent<InstantActionComponent>(actionUid);
            var helmetAction = entities.GetComponent<InstantActionComponent>(helmetThermal.ToggleActionEntity!.Value);
            Assert.That(action.UseDelay, Is.EqualTo(helmetAction.UseDelay).And.EqualTo(TimeSpan.FromSeconds(4)));
            Assert.That(thermal.PulseAccumulator, Is.GreaterThanOrEqualTo(thermal.PulseTime));
        });
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            var thermal = client.EntMan.GetComponent<ThermalVisionComponent>(client.EntMan.GetEntity(netBody));
            Assert.That(thermal.IsEquipment, Is.False);
            Assert.That(thermal.PulseTime, Is.EqualTo(3f), "Dynamically granted vision must retain its pulse duration on the client.");
            Assert.That(thermal.PulseAccumulator, Is.GreaterThanOrEqualTo(thermal.PulseTime));
            Assert.That(client.ResolveDependency<IOverlayManager>().HasOverlay<ThermalVisionOverlay>(), Is.False);
        });

        await server.WaitAssertion(() =>
        {
            var action = entities.GetComponent<InstantActionComponent>(actionUid);
            var now = server.ResolveDependency<IGameTiming>().CurTime;
            var ev = new ToggleThermalVisionEvent();
            entities.System<SharedActionsSystem>().PerformAction(body, entities.GetComponent<ActionsComponent>(body),
                actionUid, action, ev, now);
            Assert.That(ev.Handled, Is.True);
            Assert.That(action.Cooldown?.End, Is.EqualTo(now + TimeSpan.FromSeconds(4)));
            Assert.That(entities.GetComponent<ThermalVisionComponent>(body).PulseAccumulator, Is.Zero);
        });
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            var thermal = client.EntMan.GetComponent<ThermalVisionComponent>(client.EntMan.GetEntity(netBody));
            Assert.That(thermal.IsActive, Is.False, "Pulsed vision must not become a permanent toggle.");
            Assert.That(thermal.PulseAccumulator, Is.LessThan(thermal.PulseTime));
            Assert.That(client.ResolveDependency<IOverlayManager>().HasOverlay<ThermalVisionOverlay>(), Is.True);
        });

        await server.WaitAssertion(() => entities.System<SharedThermalVisionSystem>().Update(3f));
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            var thermal = client.EntMan.GetComponent<ThermalVisionComponent>(client.EntMan.GetEntity(netBody));
            Assert.That(thermal.PulseAccumulator, Is.GreaterThanOrEqualTo(thermal.PulseTime));
            Assert.That(client.ResolveDependency<IOverlayManager>().HasOverlay<ThermalVisionOverlay>(), Is.False);
        });

        // Start another pulse directly to exercise gene removal while the effect is still visible.
        await server.WaitAssertion(() =>
        {
            var ev = new ToggleThermalVisionEvent { Performer = body };
            entities.EventBus.RaiseLocalEvent(body, ev);
            Assert.That(ev.Handled, Is.True);
        });
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
            Assert.That(client.ResolveDependency<IOverlayManager>().HasOverlay<ThermalVisionOverlay>(), Is.True));

        await server.WaitAssertion(DisableGene);
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.HasComponent<ThermalVisionComponent>(client.EntMan.GetEntity(netBody)), Is.False);
            Assert.That(client.ResolveDependency<IOverlayManager>().HasOverlay<ThermalVisionOverlay>(), Is.False);
        });

        await server.WaitAssertion(() =>
        {
            Enable(entities, body, "GeneticThermalVision");
            actionUid = entities.GetComponent<ThermalVisionComponent>(body).ToggleActionEntity!.Value;
            var helmetThermal = entities.GetComponent<ThermalVisionComponent>(helmet);
            var ev = new ToggleThermalVisionEvent { Performer = body };
            entities.EventBus.RaiseLocalEvent(helmet, ev);
            Assert.That(ev.Handled, Is.True);
            DisableGene();
            Assert.That(helmetThermal.PulseAccumulator, Is.LessThan(helmetThermal.PulseTime));
            Assert.That(entities.GetComponent<ActionsComponent>(body).Actions, Does.Contain(helmetThermal.ToggleActionEntity!.Value));
        });
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
        {
            Assert.That(client.EntMan.HasComponent<ThermalVisionComponent>(client.EntMan.GetEntity(netBody)), Is.False);
            Assert.That(client.ResolveDependency<IOverlayManager>().HasOverlay<ThermalVisionOverlay>(), Is.True,
                "Removing a gene must not disable the equipped helmet's pulse.");
        });

        await server.WaitAssertion(() => entities.System<SharedThermalVisionSystem>().Update(3f));
        await pair.RunTicksSync(5);
        await client.WaitAssertion(() =>
            Assert.That(client.ResolveDependency<IOverlayManager>().HasOverlay<ThermalVisionOverlay>(), Is.False));
        await server.WaitAssertion(() =>
        {
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entities.DeleteEntity(map.MapUid);
            DeleteCipher(entities, context);
        });
        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();

        void DisableGene()
        {
            var genetics = entities.System<GeneticsSystem>();
            var genome = entities.GetComponent<GenomeComponent>(body);
            if (stabilize)
                Assert.That(genetics.TryStabilize(body), Is.True);
            else
            {
                var block = genetics.GetRound().Mutations.IndexOf("GeneticThermalVision");
                Assert.That(genetics.TrySetBlock((body, genome), block, 0, body), Is.True);
            }
            Assert.That(genome.Stability, Is.EqualTo(60));
            Assert.That(entities.HasComponent<ThermalVisionComponent>(body), Is.False);
            Assert.That(entities.GetComponent<ActionsComponent>(body).Actions, Does.Not.Contain(actionUid));
        }
    }
}
