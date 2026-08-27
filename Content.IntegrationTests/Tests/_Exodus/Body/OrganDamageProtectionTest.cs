using System.Linq;
using Content.Shared._Exodus.Body;
using Content.Shared._Shitmed.Body.Organ;
using Content.Shared.Damage.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus.Body;

[TestFixture]
[TestOf(typeof(OrganDamageProtectionSystem))]
public sealed class OrganDamageProtectionTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task RemovingOneOrganPreservesOtherModifiers(bool hiveFirst)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var body = entMan.Spawn();
            var hmeLiver = entMan.Spawn("HmeSynthLiver");
            var hiveHeart = entMan.Spawn("HiveSynthHeart");

            if (hiveFirst)
            {
                RaiseOrganEvent(entMan, hiveHeart, body, true);
                RaiseOrganEvent(entMan, hmeLiver, body, true);
            }
            else
            {
                RaiseOrganEvent(entMan, hmeLiver, body, true);
                RaiseOrganEvent(entMan, hiveHeart, body, true);
            }

            var protection = entMan.GetComponent<DamageProtectionBuffComponent>(body);
            Assert.That(protection.Modifiers.Values.Select(modifier => modifier.ID),
                Is.EquivalentTo(new[] { "HmeLiverProtection", "HiveThermalResistance" }));

            RaiseOrganEvent(entMan, hmeLiver, body, false);

            protection = entMan.GetComponent<DamageProtectionBuffComponent>(body);
            Assert.That(protection.Modifiers.Values.Select(modifier => modifier.ID),
                Is.EquivalentTo(new[] { "HiveThermalResistance" }));

            RaiseOrganEvent(entMan, hiveHeart, body, false);
            Assert.That(entMan.HasComponent<DamageProtectionBuffComponent>(body), Is.False);

            entMan.DeleteEntity(hmeLiver);
            entMan.DeleteEntity(hiveHeart);
            entMan.DeleteEntity(body);
        });

        await pair.CleanReturnAsync();
    }

    private static void RaiseOrganEvent(
        IEntityManager entMan,
        EntityUid organ,
        EntityUid body,
        bool add)
    {
        entMan.EventBus.RaiseLocalEvent(organ, new OrganComponentsModifyEvent(body, add));
    }
}
