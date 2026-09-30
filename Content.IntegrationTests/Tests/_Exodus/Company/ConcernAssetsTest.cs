using System.Linq;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Content.Shared.Research.TechnologyDisk.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Company;

[TestFixture]
public sealed class ConcernAssetsTest
{
    [Test]
    public async Task ConcernShipsAreListedAsCorporate()
    {
        await using var pair = await PoolManager.GetServerClient();
        var prototypes = pair.Server.ProtoMan;
        await pair.Server.WaitAssertion(() =>
        {
            foreach (var vessel in prototypes.EnumeratePrototypes<VesselPrototype>())
            {
                if (vessel.RequiredCompanies.Count == 0)
                    continue;
                Assert.That(vessel.Classes, Does.Contain(VesselClass.Mercenary),
                    $"{vessel.ID} is sold only to a concern, so it belongs to the corporate shipyard section.");
            }
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task StartingCasesAreLocalizedAndCarryDiskAndNetworkKey()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var prototypes = server.ProtoMan;
        var map = await pair.CreateTestMap();
        await server.WaitAssertion(() =>
        {
            var containers = entities.System<SharedContainerSystem>();
            foreach (var company in prototypes.EnumeratePrototypes<CompanyPrototype>())
            {
                foreach (var item in company.StartingItems)
                {
                    var uid = entities.SpawnEntity(item, map.GridCoords);
                    try
                    {
                        AssertLocalized(entities, uid);
                        Assert.That(containers.TryGetContainer(uid, "storagebase", out var storage), item.ToString());
                        var contents = storage!.ContainedEntities.ToList();
                        Assert.That(contents.Any(e => entities.HasComponent<TechnologyDiskComponent>(e)), $"{item} lost its technology disk.");
                        var key = contents.SingleOrDefault(e => entities.HasComponent<EncryptionKeyComponent>(e));
                        Assert.That(key, Is.Not.EqualTo(EntityUid.Invalid), $"{item} must carry the concern's encryption key.");
                        foreach (var content in contents)
                            AssertLocalized(entities, content);
                        foreach (var entry in entities.GetComponent<EncryptionKeyComponent>(key).Channels)
                        {
                            var channel = entry.Channel.Id;
                            Assert.That(prototypes.TryIndex(entry.Channel, out var radio), channel);
                            Assert.That(radio!.LongRange, channel);
                            Assert.That(radio.LocalizedName, Does.Not.StartWith("chat-radio-"), channel);
                        }
                    }
                    finally
                    {
                        entities.DeleteEntity(uid);
                    }
                }
            }
        });
        await pair.CleanReturnAsync();
    }

    private static void AssertLocalized(IEntityManager entities, EntityUid uid)
    {
        var meta = entities.GetComponent<MetaDataComponent>(uid);
        var id = meta.EntityPrototype!.ID;
        Assert.That(meta.EntityName, Does.Contain(" "), $"{id} shows a raw localization key: {meta.EntityName}");
        Assert.That(meta.EntityDescription, Does.Contain(" "), $"{id} shows a raw localization key: {meta.EntityDescription}");
    }
}
