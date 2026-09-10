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
            state.Comp.DeclarationDelay = TimeSpan.FromHours(2);
            Assert.That(wars.TryDeclareWar("TSFMC", "PDV"), Is.EqualTo(WarDeclarationResult.TooEarly));
            state.Comp.DeclarationDelay = TimeSpan.Zero;

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

            var oldDelay = state.Comp.DeclarationDelay;
            var oldDeclarations = state.Comp.Declarations;
            var oldCooldowns = state.Comp.WarCooldowns;
            var oldSequence = state.Comp.NextPeaceOfferId;
            state.Comp.DeclarationDelay = TimeSpan.Zero;
            state.Comp.Declarations = new();
            state.Comp.WarCooldowns = new();
            try
            {
                Assert.That(wars.IsRoundRunning, Is.True);
                test(entities, wars, state, ticker);
            }
            finally
            {
                state.Comp.DeclarationDelay = oldDelay;
                state.Comp.Declarations = oldDeclarations;
                state.Comp.WarCooldowns = oldCooldowns;
                state.Comp.NextPeaceOfferId = oldSequence;
                if (host is { } hostUid)
                    entities.DeleteEntity(hostUid);

                entities.EventBus.RaiseLocalEvent(state.Owner, new WarLevelChangedEvent(state.Comp.PostWar), broadcast: true);
            }
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }
}
