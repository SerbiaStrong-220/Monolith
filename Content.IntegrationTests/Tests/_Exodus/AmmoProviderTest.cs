using System.Collections.Generic;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class AmmoProviderTest
{
    [Test]
    public async Task EntityPrototypesHaveAtMostOneAmmoProvider()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var prototype in prototypes.EnumeratePrototypes<EntityPrototype>())
                {
                    if (prototype.Abstract || pair.IsTestPrototype(prototype))
                        continue;

                    var providers = new List<string>();
                    foreach (var (name, entry) in prototype.Components)
                    {
                        // These two providers do not inherit AmmoProviderComponent.
                        if (entry.Component is AmmoProviderComponent or BallisticAmmoProviderComponent or SolutionAmmoProviderComponent)
                            providers.Add(name);
                    }

                    Assert.That(providers.Count, Is.LessThanOrEqualTo(1),
                        $"{prototype.ID} has competing ammo providers: {string.Join(", ", providers)}.");
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("MobRogueSiliconCatcher", "Bola", 1, 1)]
    [TestCase("MobRogueSiliconCatcher", "Bola", 1, 3)]
    [TestCase("WeaponTurretSyndicateDisposable", "Cartridge9x19mmFMJ", 50, 1)]
    [TestCase("WeaponTurretSyndicateDisposable", "Cartridge9x19mmFMJ", 50, 3)]
    [TestCase("WeaponTurretXeno", "BulletAcid", 500, 1)]
    [TestCase("WeaponTurretXeno", "BulletAcid", 500, 3)]
    [TestCase("WeaponTurretSyndicate", "Cartridge635x40mmCaseless", 80, 1)]
    public async Task TakeAmmoReturnsOnlyRequestedAmmunition(string prototype, string ammunition, int capacity, int shots)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var weapon = entities.SpawnEntity(prototype, map.GridCoords);
            var before = new GetAmmoCountEvent();
            entities.EventBus.RaiseLocalEvent(weapon, ref before);
            Assert.That(before.Count, Is.EqualTo(capacity));
            Assert.That(before.Capacity, Is.EqualTo(capacity));

            var take = new TakeAmmoEvent(shots, new List<(EntityUid? Entity, IShootable Shootable)>(),
                map.GridCoords, weapon, willBeFired: true);
            entities.EventBus.RaiseLocalEvent(weapon, take);

            Assert.That(take.Ammo.Count, Is.EqualTo(Math.Min(shots, capacity)));
            foreach (var (ammo, _) in take.Ammo)
            {
                Assert.That(ammo, Is.Not.Null);
                Assert.That(entities.GetComponent<MetaDataComponent>(ammo!.Value).EntityPrototype?.ID,
                    Is.EqualTo(ammunition));
            }

            var after = new GetAmmoCountEvent();
            entities.EventBus.RaiseLocalEvent(weapon, ref after);
            Assert.That(after.Count, Is.EqualTo(capacity - take.Ammo.Count));
            Assert.That(after.Capacity, Is.EqualTo(capacity));
        });

        await pair.CleanReturnAsync();
    }
}
