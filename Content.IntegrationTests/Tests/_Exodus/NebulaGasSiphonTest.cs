using Content.Server._Exodus.Nebula;
using Content.Shared._Exodus.Nebula;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(NebulaGasSiphonSystem))]
public sealed class NebulaGasSiphonTest
{
    [Test]
    public async Task PrototypeChecksThreeTilesBeyondItsFootprint()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var siphonUid = entMan.Spawn("NebulaGasSiphon");
            var siphon = entMan.GetComponent<NebulaGasSiphonComponent>(siphonUid);
            var firstCheckedTile = NebulaGasSiphonSystem.GetFirstFreeTile(siphon.FootprintLength);
            var lastCheckedTile = firstCheckedTile + siphon.Range - 1;

            Assert.Multiple(() =>
            {
                Assert.That(siphon.FootprintLength, Is.EqualTo(3));
                Assert.That(siphon.Range, Is.EqualTo(3));
                Assert.That(firstCheckedTile, Is.EqualTo(2));
                Assert.That(lastCheckedTile, Is.EqualTo(4));
            });

            entMan.DeleteEntity(siphonUid);
        });

        await pair.CleanReturnAsync();
    }
}
