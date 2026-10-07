using Content.Shared._Exodus.Teleport;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class BunoRevizorTest
{
    [Test]
    public async Task RevizorCarriesRecallPadAndTwoCombatSuits()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entManager = server.EntMan;
        var protoManager = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var vessel = protoManager.Index<VesselPrototype>("BunoRevizor");
            Assert.That(vessel.RequiredCompanies, Has.Count.EqualTo(1));
            Assert.That(vessel.RequiredCompanies[0].Id, Is.EqualTo("Buno"));

            var map = entManager.System<MapSystem>().CreateMap(out var mapId);
            try
            {
                Assert.That(entManager.System<MapLoaderSystem>().TryLoadGrid(mapId, vessel.ShuttlePath, out var grid));

                var pads = 0;
                var padQuery = entManager.AllEntityQueryEnumerator<NearestShuttleTeleporterComponent, TransformComponent>();
                while (padQuery.MoveNext(out _, out _, out var xform))
                {
                    if (xform.GridUid != grid!.Value.Owner)
                        continue;

                    pads++;
                    Assert.That(xform.Anchored, Is.True);
                }

                var suits = 0;
                var metaQuery = entManager.AllEntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (metaQuery.MoveNext(out _, out var meta, out var xform))
                {
                    if (meta.EntityPrototype?.ID == "ClothingOuterHardsuitBratva" && xform.GridUid == grid!.Value.Owner)
                        suits++;
                }

                Assert.That(pads, Is.EqualTo(1));
                Assert.That(suits, Is.EqualTo(2));
            }
            finally
            {
                entManager.DeleteEntity(map);
            }
        });
        await pair.CleanReturnAsync();
    }
}
