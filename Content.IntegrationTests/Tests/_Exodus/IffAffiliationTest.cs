using Content.Server._NF.GameRule;
using Content.Shared._Exodus.Shuttles;
using Content.Shared._Exodus.Shuttles.Components;
using Content.Shared._Exodus.Territory;
using Content.Shared._Mono.Company;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(IffAffiliationSystem))]
public sealed class IffAffiliationTest
{
    [Test]
    public async Task CorporateControlChangesLabelButNotFactionColor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var entities = client.EntMan;
            var system = entities.System<IffAffiliationSystem>();
            var prototypes = client.ResolveDependency<IPrototypeManager>();
            var loc = client.ResolveDependency<ILocalizationManager>();
            var grid = entities.Spawn();
            var territory = entities.AddComponent<GridTerritoryComponent>(grid);
            var shuttles = entities.System<SharedShuttleSystem>();
            territory.Radius = 2500;
            territory.ColorPoiByFaction = true;
            territory.NeutralPoiColor = Color.Gray;
            territory.ControllingFaction = "TSFMC";

            // Territories also work without the optional display component.
            Assert.That(system.UsesFactionColor(grid), Is.True);
            Assert.That(system.TryGetLabel(grid, out var label), Is.True);
            Assert.That(label, Is.EqualTo(loc.GetString("exodus-iff-no-corporate-control")));
            Assert.That(system.HasCorporateControlLabel(grid), Is.False);

            Assert.That(shuttles.GetFtlIFFLabel(grid), Does.Not.Contain("\n"));
            Assert.That(shuttles.GetFtlIFFLabel(grid, self: true), Does.Not.Contain("\n"));

            territory.CorporateController = "Colonial";
            Assert.That(system.HasCorporateControlLabel(grid), Is.True);
            // A station's own FTL console must show the same corporate line as a visiting ship.
            var ftlLabel = shuttles.GetFtlIFFLabel(grid);
            Assert.That(ftlLabel, Does.Contain("\n"));
            Assert.That(ftlLabel, Does.Contain(loc.GetString(prototypes.Index<CompanyPrototype>("Colonial").Name)));
            Assert.That(shuttles.GetFtlIFFLabel(grid, self: true), Is.EqualTo(ftlLabel));
            Assert.That(system.TryGetLabel(grid, out label), Is.True);
            Assert.That(label, Is.EqualTo(loc.GetString(prototypes.Index<CompanyPrototype>("Colonial").Name)));
            Assert.That(system.TryGetColor(grid, out var color), Is.True);
            Assert.That(color, Is.EqualTo(prototypes.Index<TerritoryFactionPrototype>("TSFMC").Color));

            territory.CorporateController = "TSFCivilian";
            Assert.That(system.TryGetLabel(grid, out label), Is.True);
            Assert.That(label, Is.EqualTo(loc.GetString(prototypes.Index<CompanyPrototype>("TSFCivilian").Name)));
            Assert.That(system.TryGetColor(grid, out var changedColor), Is.True);
            Assert.That(changedColor, Is.EqualTo(color));

            territory.CorporateController = null;
            territory.ControllingFaction = null;
            Assert.That(system.HasCorporateControlLabel(grid), Is.False);
            Assert.That(shuttles.GetFtlIFFLabel(grid, self: true), Does.Not.Contain("\n"));
            Assert.That(system.TryGetLabel(grid, out label), Is.True);
            Assert.That(label, Is.EqualTo(loc.GetString("exodus-iff-no-faction-control")));
            Assert.That(system.TryGetColor(grid, out color), Is.True);
            Assert.That(color, Is.EqualTo(territory.NeutralPoiColor));

            // Even stale corporate state must not conceal the absence of a faction claim.
            var display = entities.AddComponent<IffAffiliationComponent>(grid);
            territory.CorporateController = "Colonial";
            Assert.That(system.TryGetLabel(grid, out label), Is.True);
            Assert.That(label, Is.EqualTo(loc.GetString("exodus-iff-no-faction-control")));
            Assert.That(system.HasCorporateControlLabel(grid), Is.False);
            territory.CorporateController = null;
            territory.ControllingFaction = "TSFMC";
            var faction = prototypes.Index<TerritoryFactionPrototype>("TSFMC");
            Assert.That(system.TryGetLabel(grid, out label), Is.True);
            Assert.That(label, Is.EqualTo(loc.GetString("exodus-iff-no-corporate-control")));

            // A fixed organization is independent of either capture layer.
            display.Mode = IffAffiliationMode.FixedCompany;
            Assert.That(system.HasCorporateControlLabel(grid), Is.False);
            display.Company = "TSFCivilian";
            territory.CorporateController = "Colonial";
            Assert.That(system.TryGetLabel(grid, out label), Is.True);
            Assert.That(label, Is.EqualTo(loc.GetString(prototypes.Index<CompanyPrototype>("TSFCivilian").Name)));
            Assert.That(system.TryGetColor(grid, out color), Is.True);
            Assert.That(color, Is.EqualTo(faction.Color));

            // Disabling the line changes neither the faction controller nor its color.
            display.Mode = IffAffiliationMode.None;
            Assert.That(system.HasCorporateControlLabel(grid), Is.False);
            Assert.That(system.TryGetLabel(grid, out label), Is.True);
            Assert.That(label, Is.Empty);
            Assert.That(system.TryGetColor(grid, out color), Is.True);
            Assert.That(color, Is.EqualTo(faction.Color));
            Assert.That(territory.ControllingFaction?.Id, Is.EqualTo("TSFMC"));
            Assert.That(territory.CorporateController?.Id, Is.EqualTo("Colonial"));

            display.Mode = IffAffiliationMode.CorporateControl;
            territory.Claimable = false;
            Assert.That(system.HasCorporateControlLabel(grid), Is.False);
            Assert.That(system.TryGetLabel(grid, out label), Is.True);
            Assert.That(label, Is.Empty);
            entities.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ExplicitAffiliationPreservesIffVisibilityAndHubColor()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var entities = client.EntMan;
            var system = entities.System<IffAffiliationSystem>();
            var shuttles = entities.System<SharedShuttleSystem>();
            var prototypes = client.ResolveDependency<IPrototypeManager>();
            var loc = client.ResolveDependency<ILocalizationManager>();
            var grid = entities.Spawn();
            var iff = entities.AddComponent<IFFComponent>(grid);
            shuttles.SetIFFColor(grid, Color.Green, iff);
            var display = entities.AddComponent<IffAffiliationComponent>(grid);
            display.Mode = IffAffiliationMode.Faction;
            display.Faction = "PDV";
            var faction = prototypes.Index<TerritoryFactionPrototype>("PDV");
            var factionName = loc.GetString(faction.DisplayName ?? faction.RadarLabel);

            Assert.That(system.TryGetLabel(grid, out var label), Is.True);
            Assert.That(label, Is.EqualTo(factionName));
            Assert.That(system.HasCorporateControlLabel(grid), Is.False);
            Assert.That(shuttles.GetIFFLabel(grid), Does.Contain(factionName));
            Assert.That(shuttles.GetIFFColor(grid), Is.EqualTo(faction.Color));
            Assert.That(shuttles.GetIFFColor(grid, self: true), Is.EqualTo(IFFComponent.SelfColor));
            Assert.That(shuttles.GetIFFLabel(grid, self: true), Is.EqualTo(entities.GetComponent<MetaDataComponent>(grid).EntityName));

            shuttles.AddIFFFlag(grid, IFFFlags.HideLabel, iff);
            Assert.That(shuttles.GetIFFLabel(grid), Is.Null);
            Assert.That(shuttles.GetFtlIFFLabel(grid), Is.Null);
            shuttles.RemoveIFFFlag(grid, IFFFlags.HideLabel, iff);
            shuttles.AddIFFFlag(grid, IFFFlags.Hide, iff);
            Assert.That(shuttles.GetIFFLabel(grid), Is.Null);
            Assert.That(shuttles.GetFtlIFFLabel(grid), Is.Null);
            shuttles.RemoveIFFFlag(grid, IFFFlags.Hide, iff);

            display.Mode = IffAffiliationMode.FixedCompany;
            display.Company = "Colonial";
            display.Faction = null;
            Assert.That(system.TryGetLabel(grid, out label), Is.True);
            Assert.That(label, Is.EqualTo(loc.GetString(prototypes.Index<CompanyPrototype>("Colonial").Name)));
            Assert.That(shuttles.GetIFFColor(grid), Is.EqualTo(iff.Color));
            Assert.That(entities.HasComponent<CompanyComponent>(grid), Is.False);
            Assert.That(entities.HasComponent<GridTerritoryComponent>(grid), Is.False);

            display.Mode = IffAffiliationMode.None;
            Assert.That(shuttles.GetIFFLabel(grid), Is.EqualTo(loc.GetString("shuttle-console-unknown")));
            display.Mode = IffAffiliationMode.CorporateControl;
            Assert.That(shuttles.GetIFFLabel(grid), Is.EqualTo(loc.GetString("shuttle-console-unknown")));
            entities.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task UnconfiguredCompanyShipRetainsLegacyDisplay()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var client = pair.Client;

        await client.WaitAssertion(() =>
        {
            var entities = client.EntMan;
            var system = entities.System<IffAffiliationSystem>();
            var grid = entities.Spawn();
            var loc = client.ResolveDependency<ILocalizationManager>();
            Assert.That(entities.System<SharedShuttleSystem>().GetIFFLabel(grid),
                Does.Contain(loc.GetString("shuttle-console-company-unknown")));
            var company = entities.AddComponent<CompanyComponent>(grid);
            company.CompanyName = "Colonial";
            var iff = entities.AddComponent<IFFComponent>(grid);
            entities.System<SharedShuttleSystem>().SetIFFColor(grid, Color.Orange, iff);

            Assert.That(system.UsesFactionColor(grid), Is.False);
            Assert.That(system.TryGetLabel(grid, out _), Is.False);
            Assert.That(system.TryGetColor(grid, out _), Is.False);
            Assert.That(entities.System<SharedShuttleSystem>().GetIFFColor(grid), Is.EqualTo(iff.Color));
            entities.DeleteEntity(grid);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("TSFMCIndustry", IffAffiliationMode.CorporateControl)]
    [TestCase("TSFMCHalcyon", IffAffiliationMode.CorporateControl)]
    [TestCase("HeliosFortress", IffAffiliationMode.CorporateControl)]
    [TestCase("Jupiter", IffAffiliationMode.Faction)]
    [TestCase("ADSKashalot", IffAffiliationMode.Faction)]
    [TestCase("Zvezda", IffAffiliationMode.CorporateControl)]
    public async Task PoiInheritancePreservesExplicitMode(string poiId, IffAffiliationMode mode)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var poi = server.ResolveDependency<IPrototypeManager>().Index<PointOfInterestPrototype>(poiId);
            Assert.That(poi.AddComponents.TryGetValue("IffAffiliation", out var entry), Is.True);
            Assert.That(((IffAffiliationComponent) entry!.Component).Mode, Is.EqualTo(mode));
        });

        await pair.CleanReturnAsync();
    }

    [TestCase("ADSShuttleEmergencyBeaconZenith", "Khsira")]
    [TestCase("AsakimShuttleEmergencyBeaconHorizont", "Khsira")]
    [TestCase("MarsocArcturusArrivalBeacon", "TSFMC")]
    [TestCase("TarkhanJupiterArrivalBeacon", "PDV")]
    public async Task BeaconAndLuxuryAffiliationsAreConfigured(string beaconId, string faction)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;

        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var beacon = prototypes.Index<EntityPrototype>(beaconId);
            var config = (ShuttleEventBeaconComponent) beacon.Components["ShuttleEventBeacon"].Component;
            Assert.That(config.ShuttleFaction?.Id, Is.EqualTo(faction));

            var luxury = prototypes.Index<VesselPrototype>("Luxury");
            var display = (IffAffiliationComponent) luxury.AddComponents["IffAffiliation"].Component;
            Assert.That(display.Mode, Is.EqualTo(IffAffiliationMode.Faction));
            Assert.That(display.Faction?.Id, Is.EqualTo("Khsira"));
        });

        await pair.CleanReturnAsync();
    }
}
