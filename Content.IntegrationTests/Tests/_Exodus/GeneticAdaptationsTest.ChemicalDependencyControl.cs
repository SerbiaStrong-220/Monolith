using System.Collections.Generic;
using Content.Server._Exodus.Chemistry;
using Content.Server._Exodus.Genetics;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Chemistry;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [TestCase("disconnect", 10)]
    [TestCase("disconnect", -119)]
    [TestCase("disconnect", -300)]
    [TestCase("visit", -300)]
    [TestCase("transfer", -300)]
    public async Task ChemicalDependencyPausesWithoutPlayerControl(string absence, int reserveSeconds)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false,
            Dirty = true,
        });
        var server = pair.Server;
        var entities = server.EntMan;
        var timing = server.ResolveDependency<IGameTiming>();
        var map = await pair.CreateTestMap();
        var player = pair.Player!;
        EntityUid body = default;
        EntityUid mind = default;
        ChemicalDependencyComponent dependency = default!;
        DamageableComponent damage = default!;
        var context = string.Empty;
        (TimeSpan Reserve, int Stage, FixedPoint2 Poison) paused = default;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobAsakim", map.MapCoords);
            context = entities.GetComponent<GenomeComponent>(body).Context;
            var minds = entities.System<SharedMindSystem>();
            mind = minds.CreateMind(player.UserId).Owner;
            minds.TransferTo(mind, body);
            Assert.That(player.Status, Is.EqualTo(SessionStatus.InGame));
            Assert.That(player.AttachedEntity, Is.EqualTo(body));

            dependency = entities.GetComponent<ChemicalDependencyComponent>(body);
            damage = entities.GetComponent<DamageableComponent>(body);
            entities.System<DamageableSystem>().TryChangeDamage(body,
                new DamageSpecifier { DamageDict = { ["Poison"] = 5 } }, true);
            dependency.Reserve = TimeSpan.FromSeconds(reserveSeconds) + dependency.UpdateInterval;
            TickDependency();
            Assert.That(dependency.Reserve, Is.EqualTo(TimeSpan.FromSeconds(reserveSeconds)));
            Assert.That(dependency.Stage, Is.EqualTo(reserveSeconds > 0 ? -1 : reserveSeconds > -300 ? 0 : 2));
            paused = Snapshot();

            if (absence == "disconnect")
                return;

            var ghost = entities.SpawnEntity(GameTicker.ObserverPrototypeName, map.MapCoords);
            if (absence == "visit")
                minds.Visit(mind, ghost);
            else
                minds.TransferTo(mind, ghost);
            Assert.That(player.AttachedEntity, Is.EqualTo(ghost));
        });

        if (absence == "disconnect")
            await pair.Disconnect("Chemical dependency pause regression test");

        await pair.RunTicksSync(timing.TickRate * 3);
        await server.WaitAssertion(() =>
        {
            if (absence == "disconnect")
                Assert.That(player.Status, Is.EqualTo(SessionStatus.Disconnected));
            Assert.That(entities.HasComponent<ActorComponent>(body), Is.False);
            Assert.That(Snapshot(), Is.EqualTo(paused), "Absence must preserve the reserve, withdrawal stage and existing damage.");
            Assert.That(dependency.NextUpdate, Is.GreaterThan(timing.CurTime), "Paused ticks must not leave a backlog.");
            Assert.That(dependency.NextUpdate, Is.LessThanOrEqualTo(timing.CurTime + dependency.UpdateInterval));
        });

        if (absence == "disconnect")
        {
            await Task.WhenAll(pair.Client.WaitIdleAsync(), server.WaitIdleAsync());
            pair.Client.SetConnectTarget(server);
            var net = pair.Client.ResolveDependency<IClientNetManager>();
            await pair.Client.WaitPost(() => net.ClientConnect(null!, 0, player.Name));
            await pair.RunTicksSync(5);
        }
        else
        {
            await server.WaitAssertion(() =>
            {
                var minds = entities.System<SharedMindSystem>();
                if (absence == "visit")
                    minds.UnVisit(mind);
                else
                    minds.TransferTo(mind, body);
                Assert.That(Snapshot(), Is.EqualTo(paused), "Returning must not refill the reserve or remove existing withdrawal.");
            });
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(pair.Player!.UserId, Is.EqualTo(player.UserId));
            Assert.That(pair.Player.Status, Is.EqualTo(SessionStatus.InGame));
            Assert.That(pair.Player.AttachedEntity, Is.EqualTo(body));
            // Reconnecting advances the pair briefly; at most one regular update can become due.
            Assert.That(dependency.Reserve, Is.InRange(paused.Reserve - dependency.UpdateInterval, paused.Reserve));
            Assert.That(Snapshot().Poison.Float(), Is.InRange(paused.Poison.Float(), paused.Poison.Float() + 0.5f));

            var before = Snapshot();
            TickDependency();
            var expectedReserve = before.Reserve - dependency.UpdateInterval;
            if (expectedReserve < -dependency.MaxDeficit)
                expectedReserve = -dependency.MaxDeficit;
            Assert.That(dependency.Reserve, Is.EqualTo(expectedReserve), "Control must resume exactly one regular update.");
            var expectedPoison = before.Poison + (reserveSeconds == -300 ? FixedPoint2.New(0.5) : FixedPoint2.Zero);
            Assert.That(Snapshot().Poison, Is.EqualTo(expectedPoison));
            var resumed = Snapshot();
            entities.System<ChemicalDependencySystem>().Update(0);
            Assert.That(Snapshot(), Is.EqualTo(resumed), "The same timestamp must not replay missed withdrawal ticks.");

            entities.System<SharedMindSystem>().TransferTo(mind, null, createGhost: false);
            entities.DeleteEntity(mind);
            entities.DeleteEntity(body);
            DeleteCipher(entities, context);
        });
        await pair.RunTicksSync(2);
        await pair.CleanReturnAsync();

        void TickDependency()
        {
            dependency.NextUpdate = timing.CurTime;
            entities.System<ChemicalDependencySystem>().Update(0);
        }

        (TimeSpan Reserve, int Stage, FixedPoint2 Poison) Snapshot()
        {
            return (dependency.Reserve, dependency.Stage, damage.Damage.DamageDict.GetValueOrDefault("Poison"));
        }
    }
}
