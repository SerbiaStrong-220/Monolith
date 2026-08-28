using Content.Server._Exodus.ShipShields;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.ShipShields;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(LayeredShipShieldSystem))]
public sealed class LayeredShipShieldTest
{
    [TestCase(101f, 2, 51f, true)]
    [TestCase(151f, 1, 51f, true)]
    [TestCase(251f, 1, 151f, false)]
    public async Task OverloadCarriesDamageAcrossLayers(
        float incomingDamage,
        int expectedLayers,
        float expectedDamage,
        bool expectedCancelled)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var (emitterUid, shieldUid, emitter, layered, visuals) = SpawnLayeredEmitter(entMan);
            emitter.Damage = incomingDamage;

            var attempt = new ShipShieldOverloadAttemptEvent(ShipShieldOverloadCause.Damage, true);
            entMan.EventBus.RaiseLocalEvent(emitterUid, ref attempt);

            Assert.Multiple(() =>
            {
                Assert.That(layered.ActiveLayerCount, Is.EqualTo(expectedLayers));
                Assert.That(visuals.LayerCount, Is.EqualTo(expectedLayers));
                Assert.That(emitter.Damage, Is.EqualTo(expectedDamage).Within(0.001f));
                Assert.That(attempt.Cancelled, Is.EqualTo(expectedCancelled));
            });

            entMan.DeleteEntity(shieldUid);
            entMan.DeleteEntity(emitterUid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task SeparateOverloadsCanCollapseLayersInSameTick()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var (emitterUid, shieldUid, emitter, layered, visuals) = SpawnLayeredEmitter(entMan);

            emitter.Damage = 101f;
            var firstAttempt = new ShipShieldOverloadAttemptEvent(ShipShieldOverloadCause.Damage, true);
            entMan.EventBus.RaiseLocalEvent(emitterUid, ref firstAttempt);

            emitter.Damage = 101f;
            var secondAttempt = new ShipShieldOverloadAttemptEvent(ShipShieldOverloadCause.Damage, true);
            entMan.EventBus.RaiseLocalEvent(emitterUid, ref secondAttempt);

            Assert.Multiple(() =>
            {
                Assert.That(firstAttempt.Cancelled, Is.True);
                Assert.That(secondAttempt.Cancelled, Is.True);
                Assert.That(layered.ActiveLayerCount, Is.EqualTo(1));
                Assert.That(visuals.LayerCount, Is.EqualTo(1));
                Assert.That(emitter.Damage, Is.EqualTo(51f).Within(0.001f));
            });

            entMan.DeleteEntity(shieldUid);
            entMan.DeleteEntity(emitterUid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task Ms1000PrototypeHasThreeIndependentLayers()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var emitterUid = entMan.Spawn("ShieldGeneratorMS1000");
            var layered = entMan.GetComponent<LayeredShipShieldComponent>(emitterUid);

            Assert.Multiple(() =>
            {
                Assert.That(layered.LayerCount, Is.EqualTo(3));
                Assert.That(layered.ActiveLayerCount, Is.EqualTo(3));
            });

            entMan.DeleteEntity(emitterUid);
        });

        await pair.CleanReturnAsync();
    }

    private static (EntityUid EmitterUid,
        EntityUid ShieldUid,
        ShipShieldEmitterComponent Emitter,
        LayeredShipShieldComponent Layered,
        ShipShieldVisualsComponent Visuals) SpawnLayeredEmitter(IEntityManager entMan)
    {
        var emitterUid = entMan.Spawn();
        var shieldUid = entMan.Spawn();
        var emitter = entMan.AddComponent<ShipShieldEmitterComponent>(emitterUid);
        var layered = entMan.AddComponent<LayeredShipShieldComponent>(emitterUid);
        var visuals = entMan.AddComponent<ShipShieldVisualsComponent>(shieldUid);

        emitter.Shield = shieldUid;
        emitter.DamageLimit = 100f;
        emitter.MaxDraw = 1000f;
        emitter.PowerModifier = 1f;
        emitter.DamageExp = 1f;
        layered.LayerCount = 3;
        layered.ActiveLayerCount = 3;
        layered.CollapseDamageFraction = 0.5f;

        return (emitterUid, shieldUid, emitter, layered, visuals);
    }
}
