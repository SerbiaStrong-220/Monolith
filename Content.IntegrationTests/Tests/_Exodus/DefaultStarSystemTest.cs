using System.Collections.Generic;
using Content.Server._Exodus.StarSystem;
using Content.Server._FarHorizons.StarSystem;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.StarSystem;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(DefaultStarSystemSystem))]
public sealed class DefaultStarSystemTest
{
    [Test]
    public async Task GeneratesWithoutPresetRuleAndDoesNotDuplicatePlanets()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.ResolveDependency<IEntityManager>();
        var config = server.ResolveDependency<IConfigurationManager>();

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var generator = entities.System<DefaultStarSystemSystem>();
            var stars = entities.System<StarSystemMapSystem>();
            var prototype = server.ResolveDependency<IPrototypeManager>().Index<StarSystemPrototype>("SystemKyphrus");
            var mapUid = maps.CreateMap(out var mapId, runMapInit: false);
            var previous = config.GetCVar(EXCVars.DefaultStarSystem);
            try
            {
                config.SetCVar(EXCVars.DefaultStarSystem, string.Empty);
                generator.EnsureDefaultSystem(mapId);
                Assert.That(entities.HasComponent<StarSystemMapComponent>(mapUid), Is.False);

                config.SetCVar(EXCVars.DefaultStarSystem, prototype.ID);
                generator.EnsureDefaultSystem(mapId);
                var component = entities.GetComponent<StarSystemMapComponent>(mapUid);
                Assert.That(component.StarSystem, Is.Not.Null);
                Assert.That(component.System?.Id, Is.EqualTo(prototype.ID));
                var original = new HashSet<EntityUid>();
                var query = entities.AllEntityQueryEnumerator<PlanetMarkerComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var marker, out var transform))
                {
                    if (transform.MapID != mapId)
                        continue;

                    original.Add(uid);
                    Assert.That(marker.Planet, Is.Not.Null);
                    Assert.That(marker.RadarRange, Is.EqualTo(10000f));
                }
                Assert.That(original, Has.Count.EqualTo(prototype.Planets.Count));

                generator.EnsureDefaultSystem(mapId);
                stars.SetSystem((mapUid, component), prototype.ID);
                var remaining = new HashSet<EntityUid>();
                query = entities.AllEntityQueryEnumerator<PlanetMarkerComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out _, out var transform))
                {
                    if (transform.MapID == mapId)
                        remaining.Add(uid);
                }
                Assert.That(remaining, Is.EquivalentTo(original));
            }
            finally
            {
                config.SetCVar(EXCVars.DefaultStarSystem, previous);
                entities.DeleteEntity(mapUid);
            }
        });
        await pair.CleanReturnAsync();
    }
}
