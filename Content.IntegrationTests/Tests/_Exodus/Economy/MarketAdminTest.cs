using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Content.Server._Exodus.Economy;
using Content.Server._Exodus.Economy.Admin;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Economy.Admin;
using Content.Shared.Administration;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketAdminTest
{
    [Test]
    public async Task FullResetClearsOnlyQuotesAfterThreeServerConfirmedSteps()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Fresh = true,
            Destructive = true,
            AdminLogsEnabled = true,
            DummyTicker = false,
        });
        var server = pair.Server;
        var settings = server.System<MarketSettingsSystem>();
        var market = server.System<DynamicMarketSystem>();
        await PoolManager.WaitUntil(server, () => market.AdminQuotesReady);
        var database = server.ResolveDependency<IServerDbManager>();
        const string storedSettings = "settings-must-survive-a-quote-reset";
        Assert.That(await database.TrySaveEconomyMarketSettings(0, storedSettings), Is.True);
        MarketAdminEui window = null;
        var quoteSaved = false;
        string actorId = null;
        await server.WaitAssertion(() =>
        {
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            actorId = player.UserId.ToString();
            var admins = server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(player);
            admins.GetAdminData(player)!.Flags = AdminFlags.EconomyDB;
            server.ResolveDependency<IConfigurationManager>().SetCVar(EXCVars.DynamicMarketPersist, true);
            var system = server.System<MarketAdminSystem>();
            // Invalid drafts must be rejected and auditable without serializing NaN as a JSON number.
            system.ApplySettings(player, new MarketAdminApplySettingsMessage(settings.GetSnapshot().Revision,
                new MarketGlobalSettingsOverride(ImpactStrength: double.NaN), new()), (success, failure) =>
            {
                Assert.That(success, Is.False);
                Assert.That(failure, Is.EqualTo("economy-admin-error-invalid"));
            });
            system.SetQuote(player, "stack:Steel", 2, (success, failure) =>
            {
                Assert.That(success, Is.True, failure);
                quoteSaved = true;
            });
            window = new MarketAdminEui(system, settings, admins, server.ResolveDependency<IGameTiming>());
            server.ResolveDependency<EuiManager>().OpenEui(window, player);
        });
        await PoolManager.WaitUntil(server, () => quoteSaved);
        Assert.That(await database.GetAllEconomyMarketQuotes(), Has.Count.EqualTo(1));
        await server.WaitAssertion(() =>
        {
            window.HandleMessage(new MarketAdminBeginResetAllMessage());
            for (var stage = 1; stage <= 3; stage++)
            {
                var state = (MarketAdminState) window.GetNewState();
                Assert.That(state.ConfirmationStage, Is.EqualTo(stage));
                Assert.That(market.GetAllQuotes(), Has.Count.EqualTo(1));
                window.HandleMessage(new MarketAdminConfirmMutationMessage(stage, state.ConfirmationToken));
            }
        });
        await PoolManager.WaitUntil(server, () => !market.AdminQuotesBusy);
        await server.WaitAssertion(() =>
        {
            var state = (MarketAdminState) window.GetNewState();
            Assert.That(state.StatusSuccess, Is.True, state.Status);
            Assert.That(state.ConfirmationStage, Is.Zero);
            Assert.That(market.GetAllQuotes(), Is.Empty);
            window.Close();
        });
        Assert.That(await database.GetAllEconomyMarketQuotes(), Is.Empty);
        Assert.That((await database.GetEconomyMarketSettings())?.Settings, Is.EqualTo(storedSettings));
        var audit = server.ResolveDependency<IAdminLogManager>();
        var logs = await audit.CurrentRoundLogs(new LogFilter { Search = "Economy ALL quotes reset" });
        Assert.Multiple(() =>
        {
            Assert.That(logs.Any(log => log.Message.Contains($"requested by {actorId}") &&
                                       log.Message.Contains("three confirmations accepted")), Is.True,
                "The actor and confirmed request must be audited before the asynchronous write.");
            Assert.That(logs.Any(log => log.Message.Contains($"reset by {actorId}") &&
                                       log.Message.Contains("saved=True")), Is.True,
                "The committed reset outcome must be audited for the same actor.");
        });
        await server.WaitAssertion(() =>
        {
            // Detach while player data still exists; destructive shutdown clears it before flushing entities.
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            server.PlayerMan.SetAttachedEntity(player, null);
            Assert.That(player.AttachedEntity, Is.Null);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ScopedResetsPreserveOtherGroupsAndRejectInvalidOrNonPersistentEdits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Fresh = true,
            Destructive = true,
        });
        var server = pair.Server;
        var market = server.System<DynamicMarketSystem>();
        var admin = server.System<MarketAdminSystem>();
        var database = server.ResolveDependency<IServerDbManager>();
        var configuration = server.ResolveDependency<IConfigurationManager>();
        var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        await PoolManager.WaitUntil(server, () => market.AdminQuotesReady);
        await server.WaitPost(() =>
        {
            var admins = server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(player);
            admins.GetAdminData(player)!.Flags = AdminFlags.EconomyDB;
        });

        await Commit(done => admin.SetQuote(player, "stack:Steel", 2, done));
        await Commit(done => admin.SetQuote(player, "gas:Oxygen", 3, done));
        await Commit(done => admin.ResetGroup(player,
            server.System<MarketCommodityGroupSystem>().GetGroup("stack:Steel"), done));
        Assert.That((await database.GetAllEconomyMarketQuotes()).Select(row => row.MarketKey),
            Is.EquivalentTo(new[] { "gas:Oxygen" }));
        await server.WaitAssertion(() =>
            Assert.That(market.GetAllQuotes().Keys, Is.EquivalentTo(new[] { "gas:Oxygen" })));

        await Commit(done => admin.ResetQuote(player, "gas:Oxygen", done));
        Assert.That(await database.GetAllEconomyMarketQuotes(), Is.Empty);
        await server.WaitAssertion(() =>
        {
            var rejected = 0;
            admin.ResetGroup(player, "NoSuchMarketGroup", (success, error) =>
            {
                Assert.That(success, Is.False);
                Assert.That(error, Is.EqualTo("economy-admin-error-group"));
                rejected++;
            });
            admin.SetQuote(player, "proto:NoSuchMarketCommodity", 2, (success, error) =>
            {
                Assert.That(success, Is.False);
                Assert.That(error, Is.EqualTo("economy-admin-error-key"));
                rejected++;
            });
            configuration.SetCVar(EXCVars.DynamicMarketPersist, false);
            admin.SetQuote(player, "stack:Steel", 2, (success, error) =>
            {
                Assert.That(success, Is.False);
                Assert.That(error, Is.EqualTo("economy-admin-error-persistence-disabled"));
                rejected++;
            });
            Assert.That(rejected, Is.EqualTo(3));
            Assert.That(market.GetAllQuotes(), Is.Empty);
            configuration.SetCVar(EXCVars.DynamicMarketPersist, true);
        });
        Assert.That(await database.GetAllEconomyMarketQuotes(), Is.Empty);
        await pair.CleanReturnAsync();

        async Task Commit(Action<Action<bool, string>> operation)
        {
            var completed = false;
            await server.WaitAssertion(() => operation((success, error) =>
            {
                Assert.That(success, Is.True, error);
                completed = true;
            }));
            await PoolManager.WaitUntil(server, () => completed);
        }
    }

    [Test]
    public async Task AccessRevocationClosesTheWindowAndRejectsReadsAndWrites()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        var market = server.System<DynamicMarketSystem>();
        var settings = server.System<MarketSettingsSystem>();
        await PoolManager.WaitUntil(server, () => market.AdminQuotesReady && settings.Ready);
        var database = server.ResolveDependency<IServerDbManager>();
        var persistedBefore = await database.GetAllEconomyMarketQuotes();
        await server.WaitAssertion(() =>
        {
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var admins = server.ResolveDependency<IAdminManager>();
            var system = server.System<MarketAdminSystem>();
            admins.PromoteHost(player);
            var data = admins.GetAdminData(player)!;
            data.Flags = AdminFlags.Admin;
            Assert.That(system.CanAdminister(player), Is.False, "Admin must not implicitly grant access to the economy.");
            Assert.That(system.TryOpen(player), Is.False);
            data.Flags = AdminFlags.EconomyDB;
            Assert.That(system.CanAdminister(player), Is.True, "EconomyDB must work without the Admin flag.");
            var window = new MarketAdminEui(system, server.System<MarketSettingsSystem>(), admins,
                server.ResolveDependency<IGameTiming>());
            server.ResolveDependency<EuiManager>().OpenEui(window, player);
            Assert.That(system.GetState(player).Settings, Is.Not.Null);
            window.HandleMessage(new MarketAdminSetQuoteMessage("stack:Steel", 2));
            var pending = (MarketAdminState) window.GetNewState();
            Assert.That(pending.ConfirmationStage, Is.EqualTo(1));
            Assert.That(market.AdminQuotesBusy, Is.False, "Beginning a confirmation cannot start a write.");
            data.Flags = AdminFlags.Admin;
            typeof(AdminManager).GetMethod("SendPermsChangedEvent", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(admins, [player]);
            Assert.Multiple(() =>
            {
                Assert.That(window.IsShutDown, Is.True);
                Assert.That(system.TryOpen(player), Is.False);
                Assert.That(system.GetState(player).Settings, Is.Null);
                Assert.That(system.GetState(player).Quotes, Is.Empty);
            });
            var called = false;
            system.ApplySettings(player, new MarketAdminApplySettingsMessage(0, new(), new()), (success, failure) =>
            {
                called = true;
                Assert.That(success, Is.False);
                Assert.That(failure, Is.EqualTo("economy-admin-error-permission"));
            });
            Assert.That(called, Is.True);
            system.SetQuote(player, "stack:Steel", 2, (success, failure) =>
            {
                Assert.That(success, Is.False);
                Assert.That(failure, Is.EqualTo("economy-admin-error-permission"));
            });
            window.HandleMessage(new MarketAdminBeginResetAllMessage());
            window.HandleMessage(new MarketAdminConfirmMutationMessage(1, pending.ConfirmationToken));
            Assert.That(window.IsShutDown, Is.True);
            Assert.That(market.AdminQuotesBusy, Is.False);
        });
        Assert.That(await database.GetAllEconomyMarketQuotes(), Is.EquivalentTo(persistedBefore));
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task QuotesAreBoundedAndStackVariantsSearchOneCanonicalRow()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var admins = server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(player);
            admins.GetAdminData(player)!.Flags = AdminFlags.EconomyDB;
            var system = server.System<MarketAdminSystem>();
            var first = system.GetState(player, pageSize: 100);
            Assert.That(first.Quotes, Has.Count.EqualTo(100));
            Assert.That(first.TotalQuotes, Is.GreaterThan(100));
            var second = system.GetState(player, page: 1, pageSize: 100);
            var keys = new HashSet<string>(first.Quotes.Select(row => row.MarketKey));
            Assert.That(second.Quotes.All(row => !keys.Contains(row.MarketKey)), Is.True);
            var stack = system.GetState(player, "SheetSteel10");
            Assert.That(stack.Quotes.Select(row => row.MarketKey), Is.EquivalentTo(new[] { "stack:Steel" }));
            Assert.Multiple(() =>
            {
                Assert.That(system.IsValidQuery(new MarketAdminQueryMessage("", null, 0, 101)), Is.False);
                Assert.That(system.IsValidQuery(new MarketAdminQueryMessage("", null, -1)), Is.False);
                Assert.That(system.IsValidQuery(new MarketAdminQueryMessage(new string('x', 129), null, 0)), Is.False);
                Assert.That(system.IsValidQuery(new MarketAdminQueryMessage("", "NoSuchMarketGroup", 0)), Is.False);
                Assert.That(system.GetState(player, page: int.MaxValue).Quotes, Has.Count.LessThanOrEqualTo(50));
            });
        });
        await pair.CleanReturnAsync();
    }
}
