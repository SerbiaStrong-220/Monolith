#nullable enable
using Content.Server._Exodus.Genetics;
using Content.Shared._DV.Weapons.Ranged.Components;
using Content.Shared._Mono.Claws;
using Content.Shared._Mono.Claws.ClawTypes;
using Content.Shared._Mono.Claws.Components;
using Content.Shared.Weapons.Melee;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class GeneticAdaptationsTest
{
    [Test]
    public async Task GeneticClawsSynchronizeOnAnExistingClientEntity()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var (server, client) = pair;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid body = default;
        NetEntity netBody = default;
        var context = string.Empty;
        var originalWide = false;
        var originalDisarm = false;

        await server.WaitAssertion(() =>
        {
            body = entities.SpawnEntity("MobHuman", map.MapCoords);
            netBody = entities.GetNetEntity(body);
            var melee = entities.GetComponent<MeleeWeaponComponent>(body);
            originalWide = melee.CanWideSwing;
            originalDisarm = melee.AltDisarm;
            server.PlayerMan.SetAttachedEntity(pair.Player!, body);
        });
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            var clientBody = client.EntMan.GetEntity(netBody);
            Assert.That(client.EntMan.EntityExists(clientBody), Is.True);
            Assert.That(client.EntMan.HasComponent<ClawsComponent>(clientBody), Is.False);
        });

        await server.WaitAssertion(() => context = Enable(entities, body, "GeneticGrowingClaws").Context);
        await AssertStage("ReptilianTinyClaws");

        await server.WaitAssertion(() =>
        {
            var claws = entities.GetComponent<ClawsComponent>(body);
            claws.ClawStage = "ReptilianBigClaws";
            entities.System<Content.Server._Mono.Claws.ClawsSystem>().UpdateClaws(body, claws);
            entities.Dirty(body, claws);
        });
        await AssertStage("ReptilianBigClaws");

        await server.WaitAssertion(() => Assert.That(entities.System<GeneticsSystem>().TryStabilize(body), Is.True));
        await pair.RunTicksSync(10);
        await client.WaitAssertion(() =>
        {
            var clientBody = client.EntMan.GetEntity(netBody);
            Assert.That(client.EntMan.HasComponent<ClawsComponent>(clientBody), Is.False);
            Assert.That(client.EntMan.HasComponent<PlayerAccuracyModifierComponent>(clientBody), Is.False);
            var melee = client.EntMan.GetComponent<MeleeWeaponComponent>(clientBody);
            Assert.That(melee.CanWideSwing, Is.EqualTo(originalWide));
            Assert.That(melee.AltDisarm, Is.EqualTo(originalDisarm));
        });

        await server.WaitAssertion(() => Enable(entities, body, "GeneticGrowingClaws"));
        await AssertStage("ReptilianBigClaws");

        await server.WaitAssertion(() =>
        {
            server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entities.DeleteEntity(map.MapUid);
            DeleteCipher(entities, context);
        });
        await pair.RunTicksSync(5);
        await pair.CleanReturnAsync();

        async Task AssertStage(ProtoId<ClawPrototype> expected)
        {
            await pair.RunTicksSync(10);
            await client.WaitAssertion(() =>
            {
                var clientBody = client.EntMan.GetEntity(netBody);
                var claws = client.EntMan.GetComponent<ClawsComponent>(clientBody);
                Assert.That(claws.ClawStage, Is.EqualTo(expected));
                Assert.That(claws.Claws, Has.Count.EqualTo(6));
                var stage = (SharpClaw) client.ResolveDependency<IPrototypeManager>().Index(expected).ClawType;
                var melee = client.EntMan.GetComponent<MeleeWeaponComponent>(clientBody);
                var accuracy = client.EntMan.GetComponent<PlayerAccuracyModifierComponent>(clientBody);
                Assert.That(melee.CanWideSwing, Is.EqualTo(stage.CanWideSwing));
                Assert.That(melee.AltDisarm, Is.EqualTo(!stage.CanWideSwing));
                Assert.That(accuracy.SpreadMultiplier, Is.EqualTo(stage.GunSpreadMultiplier));
            });
        }
    }
}
