using Content.Server._Exodus.War;
using Content.Server._Mono.AlertLevel;
using Content.Server._NF.SectorServices;
using Content.Server.GameTicking;
using Content.Shared._Exodus.Communications;
using Content.Shared._Exodus.War;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(FactionWarSystem))]
public sealed class FactionPeaceTest
{
    [Test]
    public async Task PeaceRequiresTheOtherSideAndLocksOnlyThatPair()
    {
        await WithWarState((entities, wars, state, ticker) =>
        {
            Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.Success));
            Assert.That(wars.TryDeclareWar("TSFMC", "Khsira"), Is.EqualTo(WarDeclarationResult.Success));
            Assert.That(wars.TryOfferPeace("PDV", "TSFMC"), Is.EqualTo(PeaceOfferResult.Success));
            Assert.That(wars.TryGetDeclaration(state, "TSFMC", "PDV", out var war), Is.True);
            var offerId = war.PeaceOffer!.Id;

            Assert.That(wars.TryAcceptPeace("PDV", "TSFMC", offerId), Is.EqualTo(PeaceOfferResult.NotOfferRecipient));
            Assert.That(wars.TryWithdrawPeace("TSFMC", "PDV", offerId), Is.EqualTo(PeaceOfferResult.NotOfferSender));
            Assert.That(wars.TryAcceptPeace("Khsira", "TSFMC", offerId), Is.EqualTo(PeaceOfferResult.OfferUnavailable));
            Assert.That(wars.TryGetDeclaration(state, "PDV", "TSFMC", out _), Is.True);

            Assert.That(wars.TryAcceptPeace("TSFMC", "PDV", offerId), Is.EqualTo(PeaceOfferResult.Success));
            Assert.That(wars.TryGetDeclaration(state, "TSFMC", "PDV", out _), Is.False);
            Assert.That(wars.TryGetDeclaration(state, "TSFMC", "Khsira", out _), Is.True);
            Assert.That(state.Comp.PostWar, Is.True, "A separate war is still active.");
            Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.PostWarCooldown));
            Assert.That(wars.TryDeclareWar("PDV", "TSFMC"), Is.EqualTo(WarDeclarationResult.PostWarCooldown));
            Assert.That(wars.TryDeclareWar("PDV", "Khsira"), Is.EqualTo(WarDeclarationResult.Success));

            var availableAt = wars.GetDeclarationAvailableAt(state, "TSFMC", "PDV");
            Assert.That(availableAt, Is.EqualTo(wars.GetDeclarationAvailableAt(state, "PDV", "TSFMC")));
            Assert.That(availableAt - ticker.RoundStartTimeSpan - ticker.RoundDuration(), Is.EqualTo(TimeSpan.FromMinutes(10)));

            state.Comp.WarCooldowns[0].AvailableAtRoundTime = ticker.RoundDuration();
            Assert.That(wars.TryDeclareWar("PDV", "TSFMC"), Is.EqualTo(WarDeclarationResult.Success));
            Assert.That(wars.TryAcceptPeace("TSFMC", "PDV", offerId), Is.EqualTo(PeaceOfferResult.OfferUnavailable));
        });
    }

    [Test]
    public async Task WithdrawalInvalidatesOldConfirmationsAndThrottlesNewOffers()
    {
        await WithWarState((entities, wars, state, ticker) =>
        {
            Assert.That(wars.TryOfferPeace("TSFMC", "PDV"), Is.EqualTo(PeaceOfferResult.NotAtWar));
            Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.Success));
            Assert.That(wars.TryOfferPeace("TSFMC", "PDV"), Is.EqualTo(PeaceOfferResult.Success));
            Assert.That(wars.TryGetDeclaration(state, "TSFMC", "PDV", out var war), Is.True);
            var oldId = war.PeaceOffer!.Id;

            Assert.That(wars.TryOfferPeace("PDV", "TSFMC"), Is.EqualTo(PeaceOfferResult.AlreadyPending));
            Assert.That(wars.TryWithdrawPeace("TSFMC", "PDV", oldId), Is.EqualTo(PeaceOfferResult.Success));
            Assert.That(wars.TryAcceptPeace("PDV", "TSFMC", oldId), Is.EqualTo(PeaceOfferResult.OfferUnavailable));
            Assert.That(wars.TryOfferPeace("TSFMC", "PDV"), Is.EqualTo(PeaceOfferResult.Cooldown));
            Assert.That(wars.TryOfferPeace("PDV", "TSFMC"), Is.EqualTo(PeaceOfferResult.Cooldown));
            Assert.That(war.NextPeaceOfferAtRoundTime - ticker.RoundDuration(), Is.EqualTo(TimeSpan.FromSeconds(30)));

            war.NextPeaceOfferAtRoundTime = ticker.RoundDuration();
            Assert.That(wars.TryOfferPeace("TSFMC", "PDV"), Is.EqualTo(PeaceOfferResult.Success));
            var newId = war.PeaceOffer!.Id;
            Assert.That(newId, Is.GreaterThan(oldId));
            Assert.That(wars.TryAcceptPeace("PDV", "TSFMC", oldId), Is.EqualTo(PeaceOfferResult.OfferUnavailable));
            Assert.That(wars.TryWithdrawPeace("TSFMC", "PDV", oldId), Is.EqualTo(PeaceOfferResult.OfferUnavailable));
            Assert.That(wars.TryAcceptPeace("PDV", "TSFMC", newId), Is.EqualTo(PeaceOfferResult.Success));
            Assert.That(wars.TryAcceptPeace("PDV", "TSFMC", newId), Is.EqualTo(PeaceOfferResult.NotAtWar));
        });
    }

    [Test]
    public async Task AdministrativeEndClearsOffersAndForcedWarCanBypassTheLockout()
    {
        await WithWarState((entities, wars, state, ticker) =>
        {
            Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.Success));
            Assert.That(wars.TryDeclareWar("TSFMC", "Khsira"), Is.EqualTo(WarDeclarationResult.Success));
            Assert.That(wars.TryOfferPeace("PDV", "TSFMC"), Is.EqualTo(PeaceOfferResult.Success));
            Assert.That(wars.TryGetDeclaration(state, "TSFMC", "PDV", out var war), Is.True);
            var oldId = war.PeaceOffer!.Id;

            Assert.That(wars.ClearAllWars(announce: false), Is.True);
            Assert.That(state.Comp.Declarations, Is.Empty);
            Assert.That(state.Comp.WarCooldowns, Has.Count.EqualTo(2));
            Assert.That(wars.TryDeclareWar("Khsira", "TSFMC"), Is.EqualTo(WarDeclarationResult.PostWarCooldown));
            Assert.That(wars.TryDeclareWar("TSFMC", "PDV", force: true), Is.EqualTo(WarDeclarationResult.Success));
            Assert.That(wars.TryAcceptPeace("TSFMC", "PDV", oldId), Is.EqualTo(PeaceOfferResult.OfferUnavailable));
            Assert.That(wars.TryEndWar("PDV", "TSFMC", force: true, announce: false), Is.EqualTo(WarDeclarationResult.Success));
            Assert.That(state.Comp.WarCooldowns, Has.Count.EqualTo(2), "Repeated endings must not duplicate pair timers.");
        });
    }

    [Test]
    public async Task AllianceBlocksWarUntilItIsBroken()
    {
        await WithWarState((entities, wars, state, ticker) =>
        {
            Assert.That(wars.OfferAlliance("TSFMC", "PDV"), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(wars.TryGetState(out state), Is.True);
            Assert.That(state.Comp.AllianceOffers, Has.Count.EqualTo(1));
            var offerId = state.Comp.AllianceOffers[0].Offer!.Id;

            Assert.That(wars.AcceptAlliance("TSFMC", "PDV", offerId), Is.EqualTo(AllianceOfferResult.NotOfferRecipient));
            Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.Success));
            Assert.That(wars.AcceptAlliance("PDV", "TSFMC", offerId), Is.EqualTo(AllianceOfferResult.OfferUnavailable));
            Assert.That(wars.TryEndWar("TSFMC", "PDV", force: true, announce: false), Is.EqualTo(WarDeclarationResult.Success));
            state.Comp.WarCooldowns.Clear();

            Assert.That(wars.OfferAlliance("TSFMC", "PDV"), Is.EqualTo(AllianceOfferResult.Success));
            offerId = state.Comp.AllianceOffers[0].Offer!.Id;
            Assert.That(wars.AcceptAlliance("PDV", "TSFMC", offerId), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(wars.IsAllied(state, "PDV", "TSFMC"), Is.True);
            Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.AlreadyAllied));
            Assert.That(wars.OfferAlliance("Khsira", "TSFMC"), Is.EqualTo(AllianceOfferResult.Success));

            Assert.That(wars.BreakAlliance("TSFMC", "PDV"), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(wars.IsAllied(state, "TSFMC", "PDV"), Is.False);
            Assert.That(wars.TryDeclareWar("PDV", "TSFMC"), Is.EqualTo(WarDeclarationResult.PostWarCooldown));
            var availableAt = wars.GetDeclarationAvailableAt(state, "TSFMC", "PDV");
            Assert.That(availableAt - ticker.RoundStartTimeSpan - ticker.RoundDuration(), Is.EqualTo(TimeSpan.FromMinutes(20)));
            Assert.That(wars.TryDeclareWar("TSFMC", "Khsira"), Is.EqualTo(WarDeclarationResult.Success));
        });
    }

    [Test]
    public async Task AllianceWithdrawalInvalidatesOffersAndCooldownExpiresForBothSides()
    {
        await WithWarState((entities, wars, state, ticker) =>
        {
            Assert.That(wars.OfferAlliance("TSFMC", "PDV"), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(wars.TryGetAllianceEntry(state, "PDV", "TSFMC", out var entry), Is.True);
            var oldId = entry.Offer!.Id;
            Assert.That(wars.OfferAlliance("PDV", "TSFMC"), Is.EqualTo(AllianceOfferResult.AlreadyPending));
            Assert.That(wars.WithdrawAlliance("PDV", "TSFMC", oldId), Is.EqualTo(AllianceOfferResult.NotOfferSender));
            Assert.That(wars.WithdrawAlliance("TSFMC", "PDV", oldId), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(wars.OfferAlliance("PDV", "TSFMC"), Is.EqualTo(AllianceOfferResult.Cooldown));
            entry.NextOfferAtRoundTime = ticker.RoundDuration();
            Assert.That(wars.OfferAlliance("TSFMC", "PDV"), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(wars.AcceptAlliance("PDV", "TSFMC", oldId), Is.EqualTo(AllianceOfferResult.OfferUnavailable));
            Assert.That(wars.AcceptAlliance("PDV", "TSFMC", entry.Offer!.Id), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(wars.BreakAlliance("PDV", "TSFMC"), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.PostWarCooldown));
            Assert.That(wars.TryDeclareWar("PDV", "TSFMC"), Is.EqualTo(WarDeclarationResult.PostWarCooldown));
            Assert.That(wars.BreakAlliance("TSFMC", "PDV"), Is.EqualTo(AllianceOfferResult.NotAllied));
            state.Comp.WarCooldowns[0].AvailableAtRoundTime = ticker.RoundDuration();
            Assert.That(wars.TryDeclareWar("PDV", "TSFMC"), Is.EqualTo(WarDeclarationResult.Success));
            Assert.That(wars.OfferAlliance("TSFMC", "PDV"), Is.EqualTo(AllianceOfferResult.AtWar));
        });
    }

    [Test]
    public async Task ConsolePeaceActionsRequireCommandAccess()
    {
        await WithWarState((entities, wars, state, ticker) =>
        {
            var consoleUid = entities.Spawn();
            var actor = entities.Spawn();
            var console = entities.AddComponent<WarDeclarationConsoleComponent>(consoleUid);
            console.Faction = "TSFMC";
            console.RequiredAccess.Add("Bailiff");
            try
            {
                Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.Success));
                var offer = new CommunicationsConsoleOfferPeaceMessage("PDV")
                {
                    Actor = actor,
                    UiKey = CommunicationsConsoleUiKey.Key,
                };
                entities.EventBus.RaiseLocalEvent(consoleUid, offer);
                Assert.That(wars.TryGetDeclaration(state, "TSFMC", "PDV", out var war), Is.True);
                Assert.That(war.PeaceOffer, Is.Null);

                Assert.That(wars.TryOfferPeace("PDV", "TSFMC"), Is.EqualTo(PeaceOfferResult.Success));
                var accept = new CommunicationsConsoleAcceptPeaceMessage("PDV", war.PeaceOffer!.Id)
                {
                    Actor = actor,
                    UiKey = CommunicationsConsoleUiKey.Key,
                };
                entities.EventBus.RaiseLocalEvent(consoleUid, accept);
                Assert.That(wars.TryGetDeclaration(state, "TSFMC", "PDV", out _), Is.True);

                Assert.That(wars.TryWithdrawPeace("PDV", "TSFMC", war.PeaceOffer!.Id), Is.EqualTo(PeaceOfferResult.Success));
                war.NextPeaceOfferAtRoundTime = TimeSpan.Zero;
                Assert.That(wars.TryOfferPeace("TSFMC", "PDV"), Is.EqualTo(PeaceOfferResult.Success));
                var withdraw = new CommunicationsConsoleWithdrawPeaceMessage("PDV", war.PeaceOffer!.Id)
                {
                    Actor = actor,
                    UiKey = CommunicationsConsoleUiKey.Key,
                };
                entities.EventBus.RaiseLocalEvent(consoleUid, withdraw);
                Assert.That(war.PeaceOffer, Is.Not.Null);
            }
            finally
            {
                entities.DeleteEntity(consoleUid);
                entities.DeleteEntity(actor);
            }
        });
    }

    private static async Task WithWarState(Action<IEntityManager, FactionWarSystem, Entity<WarLevelComponent>, GameTicker> test)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false });
        var server = pair.Server;
        await server.WaitAssertion(() =>
        {
            var entities = server.EntMan;
            var wars = entities.System<FactionWarSystem>();
            var ticker = entities.System<GameTicker>();
            EntityUid? host = null;
            if (!wars.TryGetState(out var state))
            {
                host = entities.Spawn();
                entities.AddComponent<StationSectorServiceHostComponent>(host.Value);
                Assert.That(wars.TryGetState(out state), Is.True);
            }

            var alerts = entities.System<FactionAlertLevelSystem>();
            Assert.That(alerts.TryGetState(out var alertState), Is.True);
            var oldCode = alertState.Comp.CurrentLevel;
            var oldDeclarations = state.Comp.Declarations;
            var oldCooldowns = state.Comp.WarCooldowns;
            var oldSequence = state.Comp.NextPeaceOfferId;
            var oldAlliances = state.Comp.Alliances;
            var oldAllianceOffers = state.Comp.AllianceOffers;
            var oldAllianceSequence = state.Comp.NextAllianceOfferId;
            alertState.Comp.CurrentLevel = "pandora";
            state.Comp.Declarations = new();
            state.Comp.WarCooldowns = new();
            state.Comp.Alliances = new();
            state.Comp.AllianceOffers = new();
            try
            {
                Assert.That(wars.IsRoundRunning, Is.True);
                test(entities, wars, state, ticker);
            }
            finally
            {
                alertState.Comp.CurrentLevel = oldCode;
                state.Comp.Declarations = oldDeclarations;
                state.Comp.WarCooldowns = oldCooldowns;
                state.Comp.NextPeaceOfferId = oldSequence;
                state.Comp.Alliances = oldAlliances;
                state.Comp.AllianceOffers = oldAllianceOffers;
                state.Comp.NextAllianceOfferId = oldAllianceSequence;
                if (host is { } hostUid)
                    entities.DeleteEntity(hostUid);

                entities.EventBus.RaiseLocalEvent(state.Owner, new WarLevelChangedEvent(state.Comp.PostWar), broadcast: true);
            }
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
