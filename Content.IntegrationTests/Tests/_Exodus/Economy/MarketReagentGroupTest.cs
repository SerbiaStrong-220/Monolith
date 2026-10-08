// (c) Space Exodus Team - EXDS-RL with CLA
using System.Collections.Generic;
using System.Linq;
using Content.Server._Exodus.Economy;
using Content.Server._Exodus.Economy.Admin;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Shared.Chemistry.Reagent;
using Robust.Server.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketReagentGroupTest
{
    [Test]
    public async Task AllReagentsHaveTheirOwnGroupIndependentOfTheirCarrier()
    {
        await using var pair = await PoolManager.GetServerClient();
        await pair.Server.WaitAssertion(() =>
        {
            var groups = pair.Server.System<MarketCommodityGroupSystem>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            foreach (var reagent in prototypes.EnumeratePrototypes<ReagentPrototype>())
            {
                var key = $"reagent:{reagent.ID}";
                Assert.That(groups.TryGetClassification(key, out var classification), Is.True, key);
                Assert.That(classification.Group.Id, Is.EqualTo("Reagents"), key);
                Assert.That(classification.Rule, Is.Null, key);
                Assert.That(groups.GetImpactMultiplier(key), Is.EqualTo(1), key);
            }
            Assert.That(groups.GetGroup("proto:Jug").Id, Is.Not.EqualTo("Reagents"));
            Assert.That(groups.GetGroup("proto:ChemMaster").Id, Is.Not.EqualTo("Reagents"));
            Assert.That(groups.TryGetClassification("reagent:ExodusUnknownReagent", out _), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ReagentReloadRebuildsClassificationWithoutAnEntityPrototypeChange()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        await pair.Server.WaitAssertion(() =>
        {
            var groups = pair.Server.System<MarketCommodityGroupSystem>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            const string key = "reagent:ExodusRuntimeMarketReagent";
            Assert.That(groups.TryGetClassification(key, out _), Is.False);
            var changed = new Dictionary<Type, HashSet<string>>();
            prototypes.LoadString("""
                - type: reagent
                  id: ExodusRuntimeMarketReagent
                  parent: Water
                  pricePerUnit: 3
                """, changed: changed);
            prototypes.ReloadPrototypes(changed);
            Assert.That(groups.TryGetClassification(key, out var classification), Is.True);
            Assert.That(classification.Group.Id, Is.EqualTo("Reagents"));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task AdminCanFilterAndSearchReagentsByTheirLocalizedNames()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var admins = server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(player);
            admins.GetAdminData(player)!.Flags = AdminFlags.EconomyDB;
            var admin = server.System<MarketAdminSystem>();
            var reagent = server.ResolveDependency<IPrototypeManager>().Index<ReagentPrototype>("Flavorol");
            var state = admin.GetState(player, "reagent:Flavorol", "Reagents");
            Assert.That(state.Quotes, Has.Count.EqualTo(1));
            Assert.That(state.Quotes[0].MarketKey, Is.EqualTo("reagent:Flavorol"));
            Assert.That(state.Quotes[0].Group.Id, Is.EqualTo("Reagents"));
            Assert.That(state.Quotes[0].Name, Is.EqualTo(reagent.LocalizedName));
            var localizedSearch = admin.GetState(player, reagent.LocalizedName, "Reagents");
            Assert.That(localizedSearch.Quotes.Any(row => row.MarketKey == "reagent:Flavorol"), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
