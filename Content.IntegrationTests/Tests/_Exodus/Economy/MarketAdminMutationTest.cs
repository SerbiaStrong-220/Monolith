using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server._Exodus.Economy;
using Content.Server._Exodus.Economy.Admin;
using Content.Server.Administration.Managers;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Shared._Exodus.CCVar;
using Content.Shared._Exodus.Economy.Admin;
using Content.Shared.Eui;
using NUnit.Framework;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus.Economy;

[TestFixture]
public sealed class MarketAdminMutationTest
{
    [Test]
    public async Task CancelReplacementAndSettingsChangesInvalidatePendingWrites()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Fresh = true,
            Destructive = true,
        });
        var server = pair.Server;
        var market = server.System<DynamicMarketSystem>();
        var settings = server.System<MarketSettingsSystem>();
        var admin = server.System<MarketAdminSystem>();
        var database = server.ResolveDependency<IServerDbManager>();
        await PoolManager.WaitUntil(server, () => market.AdminQuotesReady);
        await server.WaitAssertion(() =>
        {
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var admins = server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(player);
            var window = new MarketAdminEui(admin, settings, admins, server.ResolveDependency<IGameTiming>());
            server.ResolveDependency<EuiManager>().OpenEui(window, player);
            var revision = settings.GetSnapshot().Revision;

            window.HandleMessage(new MarketAdminApplySettingsMessage(revision,
                new MarketGlobalSettingsOverride(PurchaseMargin: 0.4), new()));
            var pending = (MarketAdminState) window.GetNewState();
            window.HandleMessage(new MarketAdminCancelMutationMessage());
            var cancelled = (MarketAdminState) window.GetNewState();
            Assert.That(cancelled.Status, Is.EqualTo("economy-admin-cancelled"));
            Assert.That(cancelled.ConfirmationStage, Is.Zero);
            window.HandleMessage(new MarketAdminConfirmMutationMessage(1, pending.ConfirmationToken));
            Assert.That(((MarketAdminState) window.GetNewState()).Status, Is.EqualTo("economy-admin-error-confirmation"));
            Assert.That(settings.GetSnapshot().Revision, Is.EqualTo(revision));

            window.HandleMessage(new MarketAdminSetQuoteMessage("stack:Steel", 1.5));
            pending = (MarketAdminState) window.GetNewState();
            window.HandleMessage(new MarketAdminSetQuoteMessage("gas:Oxygen", 2.5));
            var replacement = (MarketAdminState) window.GetNewState();
            Assert.That(replacement.ConfirmationTarget, Is.EqualTo("gas:Oxygen"));
            Assert.That(replacement.ConfirmationFactor, Is.EqualTo(2.5));
            Assert.That(replacement.ConfirmationToken, Is.Not.EqualTo(pending.ConfirmationToken));
            window.HandleMessage(new MarketAdminConfirmMutationMessage(1, pending.ConfirmationToken));
            Assert.That(((MarketAdminState) window.GetNewState()).ConfirmationStage, Is.Zero);

            window.HandleMessage(new MarketAdminSetQuoteMessage("stack:Steel", 1.5));
            pending = (MarketAdminState) window.GetNewState();
            // A changed configuration invalidates the settings and bounds shown by every active dialog.
            var configuration = server.ResolveDependency<IConfigurationManager>();
            configuration.SetCVar(EXCVars.DynamicMarketReferenceVolume,
                configuration.GetCVar(EXCVars.DynamicMarketReferenceVolume) + 1);
            var changed = (MarketAdminState) window.GetNewState();
            Assert.That(changed.ConfirmationStage, Is.Zero);
            Assert.That(changed.Status, Is.EqualTo("economy-admin-cancelled"));
            window.HandleMessage(new MarketAdminConfirmMutationMessage(1, pending.ConfirmationToken));
            Assert.That(market.GetAllQuotes(), Is.Empty);
            Assert.That(settings.Saving, Is.False);
            Assert.That(market.AdminQuotesBusy, Is.False);
            window.Close();
        });
        Assert.That(await database.GetAllEconomyMarketQuotes(), Is.Empty);
        Assert.That(await database.GetEconomyMarketSettings(), Is.Null);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EveryManualWriteWaitsForThreeConfirmationsAndOnlyWritesCapturedInput()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            Fresh = true,
            Destructive = true,
        });
        var server = pair.Server;
        var market = server.System<DynamicMarketSystem>();
        var settings = server.System<MarketSettingsSystem>();
        var admin = server.System<MarketAdminSystem>();
        var database = server.ResolveDependency<IServerDbManager>();
        await PoolManager.WaitUntil(server, () => market.AdminQuotesReady);
        MarketAdminEui window = null;
        await server.WaitAssertion(() =>
        {
            var player = server.ResolveDependency<IPlayerManager>().Sessions.Single();
            var admins = server.ResolveDependency<IAdminManager>();
            admins.PromoteHost(player);
            window = new MarketAdminEui(admin, settings, admins, server.ResolveDependency<IGameTiming>());
            server.ResolveDependency<EuiManager>().OpenEui(window, player);
        });

        foreach (var kind in new[]
        {
            MarketAdminMutationKind.ApplySettings, MarketAdminMutationKind.SetQuote,
            MarketAdminMutationKind.ResetQuote, MarketAdminMutationKind.ResetGroup, MarketAdminMutationKind.ResetAllQuotes,
        })
        {
            var seeded = false;
            await server.WaitAssertion(() => market.ApplyAdminQuotes(
                new Dictionary<string, double> { ["stack:Steel"] = 2, ["gas:Oxygen"] = 3 }, null, false,
                (success, failure) => { Assert.That(success, Is.True, failure); seeded = true; }));
            await PoolManager.WaitUntil(server, () => seeded);
            var revision = 0L;
            await server.WaitAssertion(() =>
            {
                revision = settings.GetSnapshot().Revision;
                EuiMessageBase request = kind switch
                {
                    MarketAdminMutationKind.ApplySettings => new MarketAdminApplySettingsMessage(revision,
                        new MarketGlobalSettingsOverride(PurchaseMargin: 0.25), new()),
                    MarketAdminMutationKind.SetQuote => new MarketAdminSetQuoteMessage("stack:Steel", 1.5),
                    MarketAdminMutationKind.ResetQuote => new MarketAdminResetQuoteMessage("stack:Steel"),
                    MarketAdminMutationKind.ResetGroup => new MarketAdminResetGroupMessage(
                        server.System<MarketCommodityGroupSystem>().GetGroup("stack:Steel")),
                    MarketAdminMutationKind.ResetAllQuotes => new MarketAdminBeginResetAllMessage(),
                    _ => throw new ArgumentOutOfRangeException(),
                };
                window.HandleMessage(request);
                for (var stage = 1; stage <= 2; stage++)
                {
                    var state = (MarketAdminState) window.GetNewState();
                    Assert.That(state.ConfirmationKind, Is.EqualTo(kind));
                    Assert.That(state.ConfirmationStage, Is.EqualTo(stage));
                    window.HandleMessage(new MarketAdminConfirmMutationMessage(stage, state.ConfirmationToken));
                }
                Assert.That(settings.GetSnapshot().Revision, Is.EqualTo(revision));
                Assert.That(market.GetAllQuotes()["stack:Steel"].Factor, Is.EqualTo(2));
                Assert.That(market.GetAllQuotes()["gas:Oxygen"].Factor, Is.EqualTo(3));
                // A browsing change cannot redirect the operation already being confirmed.
                window.HandleMessage(new MarketAdminQueryMessage("gas:Oxygen", null, 0));
            });
            Assert.That((await database.GetAllEconomyMarketQuotes()).Single(row => row.MarketKey == "stack:Steel").Factor,
                Is.EqualTo(2), $"{kind} must not write after only two confirmations.");
            await server.WaitAssertion(() =>
            {
                var state = (MarketAdminState) window.GetNewState();
                Assert.That(state.ConfirmationStage, Is.EqualTo(3));
                window.HandleMessage(new MarketAdminConfirmMutationMessage(3, state.ConfirmationToken));
            });
            await PoolManager.WaitUntil(server, () => !market.AdminQuotesBusy && !settings.Saving);
            await server.WaitAssertion(() =>
            {
                var state = (MarketAdminState) window.GetNewState();
                Assert.That(state.StatusSuccess, Is.True, state.Status);
                Assert.That(state.ConfirmationKind, Is.EqualTo(MarketAdminMutationKind.None));
                if (kind == MarketAdminMutationKind.ApplySettings)
                    Assert.That(settings.Current.PurchaseMargin, Is.EqualTo(0.25));
                else if (kind == MarketAdminMutationKind.SetQuote)
                    Assert.That(market.GetAllQuotes()["stack:Steel"].Factor, Is.EqualTo(1.5));
                else
                    Assert.That(market.GetAllQuotes().ContainsKey("stack:Steel"), Is.False);
                if (kind == MarketAdminMutationKind.ResetAllQuotes)
                    Assert.That(market.GetAllQuotes(), Is.Empty);
            });
        }
        await server.WaitAssertion(window.Close);
        await pair.CleanReturnAsync();
    }
}
