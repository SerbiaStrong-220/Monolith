using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._Exodus.Economy;
using Content.Server.Database;
using Content.Shared._Exodus.Economy;
using Content.Shared._Exodus.Economy.Admin;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketSettingsTest
{
    [Test]
    public async Task SavedGroupStrengthControlsActualTradingAndRejectsStaleSettings()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var server = pair.Server;
        var settings = server.System<MarketSettingsSystem>();
        var market = server.System<DynamicMarketSystem>();
        var groupSystem = server.System<MarketCommodityGroupSystem>();
        var ready = false;
        for (var i = 0; i < 300 && !ready; i++)
        {
            await server.WaitRunTicks(1);
            await server.WaitPost(() => ready = settings.Ready);
        }
        Assert.That(ready, Is.True, "Persistent settings must finish loading before trading.");

        var completed = new TaskCompletionSource<bool>();
        long revision = 0;
        await server.WaitAssertion(() =>
        {
            var snapshot = settings.GetSnapshot();
            revision = snapshot.Revision;
            settings.Apply(revision, new MarketGlobalSettingsOverride(DecayRate: 0),
                new Dictionary<ProtoId<MarketCommodityGroupPrototype>, double> { ["General"] = Math.Log(1.1) },
                (success, error) =>
                {
                    if (success)
                        completed.SetResult(true);
                    else
                        completed.SetException(new Exception(error));
                });
        });
        for (var i = 0; i < 300 && !completed.Task.IsCompleted; i++)
            await server.WaitRunTicks(1);
        Assert.That(await completed.Task.WaitAsync(TimeSpan.FromSeconds(10)), Is.True);

        await server.WaitAssertion(() =>
        {
            Assert.That(groupSystem.GetGroup("proto:ExodusUnknownAdminTestItem").Id, Is.EqualTo("General"));
            var transaction = new MarketTransactionState();
            const string key = "proto:ExodusUnknownAdminTestItem";
            market.CalculateSequentialBuyCost(key, 100, 100, 1, 1, transaction, false);
            Assert.That(transaction.Factors[key], Is.EqualTo(1.1).Within(1e-10));
            market.CalculateSequentialSellValue(key, 100, 100, 1, 1, transaction, false);
            Assert.That(transaction.Factors[key], Is.EqualTo(1).Within(1e-10));
            Assert.That(market.GetFactor(key), Is.EqualTo(1), "Preview must not mutate quotes.");
            Assert.That(settings.GetImpactStrength("Gases"), Is.EqualTo(settings.Current.ImpactStrength * 0.003f).Within(1e-10));

            var called = false;
            settings.Apply(revision, new(), new(), (success, failure) =>
            {
                called = true;
                Assert.That(success, Is.False);
                Assert.That(failure, Is.EqualTo("economy-admin-error-conflict"));
            });
            Assert.That(called, Is.True);
            Assert.That(settings.GetSnapshot().GroupOverrides["General"], Is.EqualTo(Math.Log(1.1)));
        });

        var database = server.ResolveDependency<IServerDbManager>();
        Task<(long Revision, string Settings)?> read = null!;
        await server.WaitPost(() => read = database.GetEconomyMarketSettings());
        var saved = await read;
        Assert.That(saved, Is.Not.Null);
        Assert.That(saved!.Value.Settings, Does.Contain("General"));
        await pair.CleanReturnAsync();
    }
}
