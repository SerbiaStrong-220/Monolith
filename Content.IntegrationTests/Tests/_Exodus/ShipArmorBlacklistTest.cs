using System.Numerics;
using Content.Server._Exodus.ShipArmor;
using Content.Shared._Exodus.ShipArmor;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(ShipArmorSystem))]
public sealed class ShipArmorBlacklistTest
{
    [Test]
    public async Task VinesBypassArmorWhileStructuresAndFloorRemainProtected(
        [Values("ShipArmorModule", "ShipArmorModuleBlackhawk")] string armorPrototype,
        [Values("Kudzu", "ChimeraFleshKudzu")] string vinePrototype,
        [Values] bool explosion)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var transform = entities.System<SharedTransformSystem>();
            var map = maps.CreateMap(out var mapId);
            try
            {
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
                for (var x = 0; x < 3; x++)
                    maps.SetTile(grid.Owner, grid.Comp, new Vector2i(x, 0), new Tile(1));

                var armorUid = SpawnAnchored(armorPrototype, 0);
                var vine = SpawnAnchored(vinePrototype, 1);
                var wall = SpawnAnchored("WallSolid", 2);
                var armor = entities.GetComponent<ShipArmorComponent>(armorUid);
                var incoming = FixedPoint2.New(10);
                var charge = armor.CurrentCharge;

                Assert.That(armor.TargetBlacklist, Is.Not.Null);
                Assert.That(entities.HasComponent<ShipArmorGridComponent>(grid.Owner), Is.True);

                // Exercise the damage hooks without destroying the real vine prototypes.
                Assert.That(ModifyDamage(entities, vine, incoming, explosion), Is.EqualTo(incoming));
                Assert.That(armor.CurrentCharge, Is.EqualTo(charge), "Excluded vines must not consume armor charge.");

                Assert.That(ModifyDamage(entities, wall, incoming, explosion), Is.EqualTo(FixedPoint2.Zero));
                Assert.That(armor.CurrentCharge, Is.EqualTo(charge - incoming * armor.ChargeCostMultiplier));

                // A vine on a tile must not disable protection for the underlying floor.
                charge = armor.CurrentCharge;
                var tileDamage = new ShipArmorTileDamageEvent(grid.Owner, new Vector2i(1, 0), incoming);
                entities.EventBus.RaiseLocalEvent(grid.Owner, ref tileDamage);
                Assert.That(tileDamage.Cancelled, Is.True);
                Assert.That(armor.CurrentCharge, Is.EqualTo(charge - incoming * armor.ChargeCostMultiplier));

                // The exclusion is configurable per module, not hardcoded into the damage system.
                armor.TargetBlacklist = null;
                charge = armor.CurrentCharge;
                Assert.That(ModifyDamage(entities, vine, incoming, explosion), Is.EqualTo(FixedPoint2.Zero));
                Assert.That(armor.CurrentCharge, Is.EqualTo(charge - incoming * armor.ChargeCostMultiplier));

                EntityUid SpawnAnchored(string prototype, int x)
                {
                    var uid = entities.SpawnEntity(prototype, new EntityCoordinates(grid.Owner, new Vector2(x + 0.5f, 0.5f)));
                    var xform = entities.GetComponent<TransformComponent>(uid);
                    if (!xform.Anchored)
                        transform.AnchorEntity((uid, xform));

                    Assert.That(xform.Anchored, Is.True);
                    return uid;
                }
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });

        await pair.CleanReturnAsync();
    }

    private static FixedPoint2 ModifyDamage(IEntityManager entities, EntityUid target, FixedPoint2 amount, bool explosion)
    {
        var damage = new DamageSpecifier { DamageDict = { ["Slash"] = amount } };
        if (explosion)
        {
            var attempt = new BeforeDamageChangedEvent(damage, OriginFlag: DamageableSystem.DamageOriginFlag.Explosion);
            entities.EventBus.RaiseLocalEvent(target, ref attempt);
            return attempt.Damage.GetTotal();
        }

        var modify = new DamageModifyEvent(damage);
        entities.EventBus.RaiseLocalEvent(target, modify);
        return modify.Damage.GetTotal();
    }
}
