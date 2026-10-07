using Content.Server._Exodus.Biocode;
using Content.Shared._Exodus.Biocode;
using Content.Shared.NPC.Systems;
using Content.Shared.Weapons.Melee.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(BiocodeSystem))]
public sealed class BiocodeWeaponTest
{
    [TestCase("WeaponLRC21PPLExodusAbaddon")]
    [TestCase("WeaponHeavyPulseCannonExodusAbaddon")]
    [TestCase("WeaponPulseAnnihilatorExodusAbaddon")]
    [TestCase("WeaponMurasamaExodusAbaddon")]
    [TestCase("EnergySwordExodusAbaddon")]
    [TestCase("ClothingHandsGlovesExodusAbaddon")]
    public async Task FactionMembershipControlsWeaponUse(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient();
        var map = await pair.CreateTestMap();

        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var factions = entities.System<NpcFactionSystem>();
            var biocode = entities.System<BiocodeSystem>();
            var weapon = entities.SpawnEntity(prototype, map.MapCoords);
            var user = entities.SpawnEntity(null, map.MapCoords);
            var component = entities.GetComponent<BiocodeComponent>(weapon);

            try
            {
                // Test the biocode independently of the LRC-21's separate melee wield requirement.
                entities.RemoveComponent<MeleeRequiresWieldComponent>(weapon);
                AssertAccess(false);
                factions.AddFaction((user, null), "PirateNF");
                AssertAccess(false);
                factions.AddFaction((user, null), "TSFMC");
                AssertAccess(true);
                factions.RemoveFaction((user, null), "TSFMC");
                AssertAccess(false);

                // Older biocodes without any conditions remain unrestricted.
                component.Factions = null;
                AssertAccess(true);

                // An explicitly empty faction allowlist must not grant access.
                component.Factions = [];
                AssertAccess(false);
            }
            finally
            {
                entities.DeleteEntity(weapon);
                entities.DeleteEntity(user);
            }

            void AssertAccess(bool allowed)
            {
                Assert.That(biocode.IsAllowed((weapon, component), user), Is.EqualTo(allowed));

                var shot = new AttemptShootEvent(user, null);
                entities.EventBus.RaiseLocalEvent(weapon, ref shot);
                Assert.That(shot.Cancelled, Is.EqualTo(!allowed));

                var melee = new AttemptMeleeEvent(user);
                entities.EventBus.RaiseLocalEvent(weapon, ref melee);
                Assert.That(melee.Cancelled, Is.EqualTo(!allowed));
            }
        });

        await pair.CleanReturnAsync();
    }
}
