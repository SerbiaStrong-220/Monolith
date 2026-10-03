using Content.Client.Atmos.Components;
using Content.Client.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(FireVisualizerSystem))]
public sealed class FireVisualizerTest
{
    [Test]
    public async Task InitiallyBurningEntityCreatesLightAndExtinguishingRemovesIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;
        await client.WaitAssertion(() =>
        {
            var em = client.EntMan;
            var uid = em.CreateEntityUninitialized(null);
            var sprite = em.AddComponent<SpriteComponent>(uid);
            var appearance = em.AddComponent<AppearanceComponent>(uid);
            var fire = new FireVisualsComponent
            {
                Sprite = "Effects/fire.rsi",
                NormalState = "fire",
            };
            em.AddComponent(uid, fire);
            var appearances = em.System<AppearanceSystem>();

            // Initial network state can mark an entity as burning before its components initialize.
            appearances.SetData(uid, FireVisuals.OnFire, true, appearance);
            appearances.SetData(uid, FireVisuals.FireStacks, 2f, appearance);
            em.InitializeAndStartEntity(uid);

            Assert.That(fire.LightEntity, Is.Not.Null);
            var light = fire.LightEntity!.Value;
            Assert.That(em.HasComponent<PointLightComponent>(light), Is.True);
            Assert.That(em.GetComponent<TransformComponent>(light).ParentUid, Is.EqualTo(uid));
            Assert.That(em.System<SpriteSystem>().TryGetLayer((uid, sprite), FireVisualLayers.Fire, out var layer, false), Is.True);
            Assert.That(layer!.Visible, Is.True);

            appearances.SetData(uid, FireVisuals.OnFire, false, appearance);
            appearances.OnChangeData(uid, sprite, appearance);
            Assert.That(fire.LightEntity, Is.Null);
            Assert.That(em.EntityExists(light), Is.False);
            Assert.That(layer.Visible, Is.False);
            em.DeleteEntity(uid);
        });
        await pair.CleanReturnAsync();
    }
}
