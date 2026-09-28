using System.Numerics;
using Content.Server._Exodus.Territory;
using Content.Shared._Exodus.Construction.Conditions;
using Content.Shared._Exodus.Territory;
using Content.Shared.Construction.Prototypes;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Company;

[TestFixture]
public sealed class CorporateConcernBannerTest
{
    [Test]
    public async Task OnlyConcernBannersClaimCorporateTerritory()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var prototypes = server.ResolveDependency<IPrototypeManager>();

        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var transforms = entities.System<SharedTransformSystem>();
            var territories = entities.System<GridTerritorySystem>();
            var map = maps.CreateMap(out var mapId);
            try
            {
                var grid = server.ResolveDependency<IMapManager>().CreateGridEntity(mapId);
                maps.SetTile(grid.Owner, grid.Comp, new Vector2i(0, 0), new Tile(1));
                var territory = entities.AddComponent<GridTerritoryComponent>(grid.Owner);
                territories.SetController(grid.Owner, "TSFMC");

                foreach (var concern in new[] { "Augok", "DrakeBlackArmsUSA", "HorizonHarmonyHammerMatter", "Buno" })
                {
                    var bannerId = "CompanyTerritoryBanner" + concern;
                    var construction = prototypes.Index<ConstructionPrototype>(bannerId);
                    Assert.That(HasCorporateClaimLimit(construction), Is.True, bannerId);
                    var banner = entities.SpawnEntity(bannerId, new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
                    var claim = entities.GetComponent<CompanyTerritoryBannerComponent>(banner);
                    Assert.That(claim.CanClaim, Is.True, bannerId);
                    Assert.That(claim.Company?.Id, Is.EqualTo(concern), bannerId);
                    Assert.That(transforms.AnchorEntity((banner, entities.GetComponent<TransformComponent>(banner))), Is.True, bannerId);
                    Assert.That(territory.CorporateController?.Id, Is.EqualTo(concern), bannerId);
                    Assert.That(territory.ActiveCorporateBanner, Is.EqualTo(banner), bannerId);
                    transforms.Unanchor(banner);
                    Assert.That(territory.CorporateController, Is.Null, bannerId);
                    entities.DeleteEntity(banner);
                }

                foreach (var brand in new[]
                {
                    "Drake", "SteelHammer", "Hme", "Maco", "Dme", "Aetherion", "Horizon",
                    "Unsa", "Cdm", "Ullman", "Nosske", "Blackhawk", "Hive", "Bratva",
                })
                {
                    var bannerId = "CompanyTerritoryBanner" + brand;
                    Assert.That(HasCorporateClaimLimit(prototypes.Index<ConstructionPrototype>(bannerId)), Is.False, bannerId);
                    var banner = entities.SpawnEntity(bannerId, new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
                    var claim = entities.GetComponent<CompanyTerritoryBannerComponent>(banner);
                    Assert.That(claim.CanClaim, Is.False, bannerId);
                    Assert.That(transforms.AnchorEntity((banner, entities.GetComponent<TransformComponent>(banner))), Is.True, bannerId);
                    Assert.That(territory.CorporateController, Is.Null, bannerId);
                    transforms.Unanchor(banner);
                    entities.DeleteEntity(banner);
                }

                foreach (var independent in new[] { "MMC", "Viper" })
                {
                    var banner = entities.SpawnEntity("CompanyTerritoryBanner" + independent,
                        new EntityCoordinates(grid.Owner, new Vector2(0.5f, 0.5f)));
                    Assert.That(entities.GetComponent<CompanyTerritoryBannerComponent>(banner).CanClaim, Is.True, independent);
                    entities.DeleteEntity(banner);
                }
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });

        await pair.CleanReturnAsync();

        static bool HasCorporateClaimLimit(ConstructionPrototype construction)
        {
            foreach (var condition in construction.Conditions)
            {
                if (condition is NoActiveCompanyTerritoryBannerOnGrid)
                    return true;
            }

            return false;
        }
    }
}
