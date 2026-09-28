using System.Collections.Generic;
using Content.Client.UserInterface.Systems.Chat;
using Content.Server._Exodus.Nebula.Components;
using Content.Server._Exodus.War;
using Content.Server._Mono.AlertLevel;
using Content.Server._NF.SectorServices;
using Content.Server.Power.Components;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared._Exodus.Territory;
using Content.Shared._NC.Radio;
using Content.Shared.Chat;
using Content.Shared.Emp;
using Content.Shared.Ghost;
using Content.Shared.GameTicking;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class FactionAllianceRadioTest
{
    [TestPrototypes]
    private const string Prototypes = """
        - type: territoryFaction
          id: ExodusRadioTestFaction
          radarLabel: territory-faction-ussp
          allianceRadioChannels: [ExodusRadioTestChannel, ExodusRadioTestBackup]

        - type: radioChannel
          id: ExodusRadioTestChannel
          name: chat-radio-ussp
          keycode: '①'
          frequency: 1771
          maxRange: 5

        - type: radioChannel
          id: ExodusRadioTestBackup
          name: chat-radio-ussp
          keycode: '②'
          frequency: 1772
        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task AcceptanceBreakAndForcedWarApplyToTheNextMessage(bool reverseBreak)
    {
        await WithRadioScene(scene =>
        {
            var first = scene.AddReceiver("Nfsd");
            var second = scene.AddReceiver("Freelance");
            scene.AddReceiver("Remnants");
            scene.AssertRecipients("Nfsd", [first]);
            Assert.That(scene.Wars.OfferAlliance("TSFMC", "PDV"), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(scene.Wars.TryGetAllianceEntry(scene.State, "TSFMC", "PDV", out var entry), Is.True);
            var oldOffer = entry.Offer!.Id;
            scene.AssertRecipients("Nfsd", [first]);
            Assert.That(scene.Wars.WithdrawAlliance("TSFMC", "PDV", oldOffer), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(scene.Wars.AcceptAlliance("PDV", "TSFMC", oldOffer), Is.EqualTo(AllianceOfferResult.OfferUnavailable));
            scene.AssertRecipients("Freelance", [second]);

            entry.NextOfferAtRoundTime = TimeSpan.Zero;
            scene.Ally("TSFMC", "PDV");
            scene.AssertRecipients("Nfsd", [first, second]);
            scene.AssertRecipients("Freelance", [first, second]);
            var lateReceiver = scene.AddReceiver("Freelance");
            scene.AssertRecipients("Nfsd", [first, second, lateReceiver]);
            Assert.That(scene.Entities.GetComponent<ActiveRadioComponent>(first).Channels, Is.EquivalentTo(new[] { "Nfsd" }));
            Assert.That(scene.Entities.GetComponent<ActiveRadioComponent>(second).Channels, Is.EquivalentTo(new[] { "Freelance" }));

            Assert.That(scene.Wars.BreakAlliance(reverseBreak ? "PDV" : "TSFMC", reverseBreak ? "TSFMC" : "PDV"),
                Is.EqualTo(AllianceOfferResult.Success));
            scene.AssertRecipients("Nfsd", [first]);
            scene.AssertRecipients("Freelance", [second, lateReceiver]);
            scene.Ally("TSFMC", "PDV");
            scene.AssertRecipients("Nfsd", [first, second, lateReceiver]);
            Assert.That(scene.Wars.TryDeclareWar("TSFMC", "PDV", force: true), Is.EqualTo(WarDeclarationResult.Success));
            scene.AssertRecipients("Nfsd", [first]);
            Assert.That(scene.Wars.TryEndWar("TSFMC", "PDV", force: true, announce: false), Is.EqualTo(WarDeclarationResult.Success));
            scene.AssertRecipients("Freelance", [second, lateReceiver]);
        });
    }

    [Test]
    public async Task EveryFourFactionGraphSharesOnlyDirectAllies()
    {
        await WithRadioScene(scene =>
        {
            ProtoId<TerritoryFactionPrototype>[] factions = ["TSFMC", "PDV", "Khsira", "ExodusRadioTestFaction"];
            ProtoId<RadioChannelPrototype>[] channels = ["Nfsd", "Freelance", "Remnants", "ExodusRadioTestChannel"];
            var receivers = new EntityUid[4];
            for (var i = 0; i < receivers.Length; i++)
                receivers[i] = scene.AddReceiver(channels[i]);

            (int First, int Second)[] edges = [(0, 1), (0, 2), (0, 3), (1, 2), (1, 3), (2, 3)];
            var previousMask = 0;
            for (var mask = 0; mask < 1 << edges.Length; mask++)
            {
                for (var edge = 0; edge < edges.Length; edge++)
                {
                    if (((mask ^ previousMask) & (1 << edge)) == 0)
                        continue;

                    var (first, second) = edges[edge];
                    if ((mask & (1 << edge)) != 0)
                        scene.Ally(factions[first], factions[second]);
                    else
                        Assert.That(scene.Wars.BreakAlliance(factions[first], factions[second]), Is.EqualTo(AllianceOfferResult.Success));
                }

                for (var sender = 0; sender < factions.Length; sender++)
                {
                    var expected = new List<EntityUid> { receivers[sender] };
                    for (var edge = 0; edge < edges.Length; edge++)
                    {
                        if ((mask & (1 << edge)) == 0)
                            continue;

                        var (first, second) = edges[edge];
                        if (sender == first)
                            expected.Add(receivers[second]);
                        else if (sender == second)
                            expected.Add(receivers[first]);
                    }

                    scene.AssertRecipients(channels[sender], expected);
                }

                previousMask = mask;
            }
        });
    }

    [Test]
    public async Task PrivateChannelsNativeKeysAndMultipleChannelsRemainIndependent()
    {
        await WithRadioScene(scene =>
        {
            var first = scene.AddReceiver("Nfsd");
            var second = scene.AddReceiver("Freelance");
            var both = scene.AddReceiver("Nfsd", "Freelance");
            var command = scene.AddReceiver("Command");
            var vanguard = scene.AddReceiver("VanguardCommand");
            var common = scene.AddReceiver("Common");
            var fourth = scene.AddReceiver("ExodusRadioTestChannel");
            var backup = scene.AddReceiver("ExodusRadioTestBackup");
            var all = scene.AddReceiver();
            scene.Entities.GetComponent<ActiveRadioComponent>(all).ReceiveAllChannels = true;
            scene.Ally("TSFMC", "PDV");
            scene.Ally("TSFMC", "ExodusRadioTestFaction");

            scene.AssertRecipients("Nfsd", [first, second, both, fourth, backup, all]);
            scene.AssertRecipients("Command", [command, all]);
            scene.AssertRecipients("VanguardCommand", [vanguard, all]);
            scene.AssertRecipients("Common", [common, all]);
            scene.AssertRecipients("ExodusRadioTestBackup", [first, both, backup, all]);

            // Removing a key immediately removes its allied reception as well.
            scene.Entities.GetComponent<ActiveRadioComponent>(second).Channels.Clear();
            scene.AssertRecipients("Nfsd", [first, both, fourth, backup, all]);
            Assert.That(scene.Wars.BreakAlliance("TSFMC", "PDV"), Is.EqualTo(AllianceOfferResult.Success));
            scene.AssertRecipients("Freelance", [both, all]);

            // A faction removed from diplomacy must not leave access behind in serialized alliances.
            scene.State.Comp.Factions.Remove("ExodusRadioTestFaction");
            scene.AssertRecipients("Nfsd", [first, both, all]);
        });
    }

    [Test]
    public async Task IntercomsRespectSupportedChannelsAndTunedFrequencies()
    {
        await WithRadioScene(scene =>
        {
            var first = scene.AddReceiver("Nfsd");
            var second = scene.AddReceiver("Freelance");
            var intercom = scene.AddReceiver("Freelance");
            var entities = scene.Entities;
            entities.AddComponent<RadioMicrophoneComponent>(intercom);
            ProtoId<RadioChannelPrototype> receivingChannel = "Freelance";
            var receivingFrequency = scene.Prototypes.Index(receivingChannel).Frequency;
            scene.Tune(intercom, receivingFrequency);
            var controls = entities.AddComponent<IntercomComponent>(intercom);
            controls.SupportedChannels.Add("Freelance");
            scene.Ally("TSFMC", "PDV");

            scene.AssertRecipients("Nfsd", [first, second, intercom]);
            scene.Tune(intercom, receivingFrequency + 1);
            scene.AssertRecipients("Nfsd", [first, second]);
            scene.Tune(intercom, receivingFrequency);
            controls.SupportedChannels.Clear();
            scene.AssertRecipients("Nfsd", [first, second]);
            controls.SupportedChannels.Add("Freelance");

            var ghost = scene.AddReceiver();
            entities.GetComponent<ActiveRadioComponent>(ghost).ReceiveAllChannels = true;
            entities.AddComponent<GhostComponent>(ghost);
            var filteredGhost = scene.AddReceiver("Nfsd");
            entities.AddComponent<GhostComponent>(filteredGhost);
            scene.AssertRecipients("Freelance", [first, second, intercom, ghost]);
            ProtoId<RadioChannelPrototype> transmittingChannel = "Nfsd";
            var transmitFrequency = scene.Prototypes.Index(transmittingChannel).Frequency + 1;
            scene.AssertRecipients("Nfsd", [ghost, filteredGhost], transmitFrequency);

            // Native manually tuned reception is preserved, but it does not open an allied channel.
            entities.AddComponent<RadioMicrophoneComponent>(first);
            scene.Tune(first, transmitFrequency);
            scene.AssertRecipients("Nfsd", [first, ghost, filteredGhost], transmitFrequency);
        });
    }

    [Test]
    public async Task SharingPreservesBlackoutsEmpTelecomsAndDisabledHeadsets()
    {
        await WithRadioScene(scene =>
        {
            var entities = scene.Entities;
            var first = scene.AddReceiver("Nfsd");
            var second = scene.AddReceiver("Freelance");
            scene.Ally("TSFMC", "PDV");
            scene.AssertRecipients("Nfsd", [first, second]);
            entities.AddComponent<EmpDisabledComponent>(second);
            scene.AssertRecipients("Nfsd", [first]);
            entities.RemoveComponent<EmpDisabledComponent>(second);
            entities.AddComponent<NebulaRadioBlackoutComponent>(second);
            scene.AssertRecipients("Nfsd", [first]);
            entities.RemoveComponent<NebulaRadioBlackoutComponent>(second);
            entities.AddComponent<EmpDisabledComponent>(scene.Source);
            scene.AssertRecipients("Nfsd", []);
            entities.RemoveComponent<EmpDisabledComponent>(scene.Source);
            entities.AddComponent<NebulaRadioBlackoutComponent>(scene.Source);
            scene.AssertRecipients("Nfsd", []);
            entities.RemoveComponent<NebulaRadioBlackoutComponent>(scene.Source);

            entities.RemoveComponent<TelecomExemptComponent>(scene.Source);
            scene.AssertRecipients("Nfsd", []);
            var telecom = entities.SpawnEntity(null, scene.Coordinates);
            entities.AddComponent<TelecomServerComponent>(telecom);
            var keys = entities.AddComponent<EncryptionKeyHolderComponent>(telecom);
            keys.Channels.Add(new RadioChannelEntry("Nfsd"));
            var power = entities.AddComponent<ApcPowerReceiverComponent>(telecom);
            power.Powered = true;
            scene.AssertRecipients("Nfsd", [first, second]);
            power.Powered = false;
            scene.AssertRecipients("Nfsd", []);
            power.Powered = true;

            var headset = entities.AddComponent<HeadsetComponent>(second);
            entities.System<HeadsetSystem>().SetEnabled(second, false, headset);
            entities.CullRemovedComponents();
            scene.AssertRecipients("Nfsd", [first]);
        });
    }

    [Test]
    public async Task OriginalChannelRangeAndMapRestrictionsStillApply()
    {
        await WithRadioScene(scene =>
        {
            var entities = scene.Entities;
            var first = scene.AddReceiver("Nfsd");
            var second = scene.AddReceiver("Freelance");
            var fourth = scene.AddReceiver("ExodusRadioTestChannel");
            scene.Ally("TSFMC", "PDV");
            scene.Ally("TSFMC", "ExodusRadioTestFaction");
            scene.AssertRecipients("ExodusRadioTestChannel", [first, fourth]);

            var transform = entities.System<SharedTransformSystem>();
            transform.SetCoordinates(first, new EntityCoordinates(scene.Coordinates.EntityId, 20, 0));
            scene.AssertRecipients("ExodusRadioTestChannel", [fourth]);
            transform.SetCoordinates(first, scene.Coordinates);
            var otherMap = entities.System<SharedMapSystem>().CreateMap(out _);
            transform.SetCoordinates(second, new EntityCoordinates(otherMap, 0, 0));
            scene.AssertRecipients("Nfsd", [first, fourth]);
            entities.GetComponent<ActiveRadioComponent>(second).GlobalReceive = true;
            scene.AssertRecipients("Nfsd", [first, second, fourth]);
            entities.DeleteEntity(otherMap);
        });
    }

    [Test]
    public async Task SectorCleanupAndReplacementCannotRetainAlliedReception()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var wars = entities.System<FactionWarSystem>();
            var sector = entities.System<SectorServiceSystem>();
            var host = entities.Spawn();
            entities.AddComponent<StationSectorServiceHostComponent>(host);
            Assert.That(wars.TryGetState(out var state), Is.True);
            var source = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<TelecomExemptComponent>(source);
            var scene = new RadioScene(entities, pair.Server.ProtoMan, wars, state, source, map.GridCoords);
            var first = scene.AddReceiver("Nfsd");
            var second = scene.AddReceiver("Freelance");
            scene.Ally("TSFMC", "PDV");
            scene.AssertRecipients("Nfsd", [first, second]);

            sector.OnCleanup(new RoundRestartCleanupEvent());
            Assert.That(wars.TryGetState(out _), Is.False);
            scene.AssertRecipients("Nfsd", [first]);
            entities.DeleteEntity(host);

            host = entities.Spawn();
            entities.AddComponent<StationSectorServiceHostComponent>(host);
            Assert.That(wars.TryGetState(out var replacement), Is.True);
            Assert.That(replacement.Comp.Alliances, Is.Empty);
            scene.AssertRecipients("Nfsd", [first]);
            entities.DeleteEntity(host);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MultipleHeadsetsAndIntrinsicRadioDeliverOneClientChatMessage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false, Connected = true });
        var map = await pair.CreateTestMap();
        var entities = pair.Server.EntMan;
        EntityUid host = default;
        EntityUid listener = default;
        EntityUid source = default;
        EntityUid nativeHeadset = default;
        await pair.Server.WaitAssertion(() =>
        {
            host = entities.Spawn();
            entities.AddComponent<StationSectorServiceHostComponent>(host);
            var wars = entities.System<FactionWarSystem>();
            Assert.That(wars.TryGetState(out var state), Is.True);
            source = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<TelecomExemptComponent>(source);
            listener = entities.SpawnEntity("MobHuman", map.GridCoords);
            entities.AddComponent<AllianceRadioTestReceiverComponent>(listener);
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, listener);
            var scene = new RadioScene(entities, pair.Server.ProtoMan, wars, state, source, new EntityCoordinates(listener, 0, 0));
            nativeHeadset = scene.AddReceiver("Nfsd");
            var alliedHeadset = scene.AddReceiver("Freelance");
            entities.AddComponent<HeadsetComponent>(nativeHeadset).IsEquipped = true;
            entities.AddComponent<HeadsetComponent>(alliedHeadset).IsEquipped = true;
            scene.Ally("TSFMC", "PDV");
        });
        await pair.RunTicksSync(5);

        var previousCount = 0;
        await pair.Client.WaitAssertion(() => previousCount = CountRadioMessages());
        for (var stage = 0; stage < 4; stage++)
        {
            await pair.Server.WaitAssertion(() =>
            {
                if (stage == 1)
                {
                    entities.AddComponent<IntrinsicRadioReceiverComponent>(listener);
                    entities.AddComponent<ActiveRadioComponent>(listener).Channels.Add("Freelance");
                }
                else if (stage == 2)
                {
                    Assert.That(entities.System<FactionWarSystem>().BreakAlliance("TSFMC", "PDV"), Is.EqualTo(AllianceOfferResult.Success));
                }
                else if (stage == 3)
                {
                    entities.GetComponent<ActiveRadioComponent>(nativeHeadset).Channels.Clear();
                }

                var received = entities.GetComponent<AllianceRadioTestReceiverComponent>(listener);
                received.HeadsetCount = 0;
                entities.System<RadioSystem>().SendRadioMessage(source, $"allied headset test {stage}", "Nfsd", source);
                Assert.That(received.HeadsetCount, Is.EqualTo(stage == 3 ? 0 : 1), "Headset listeners must still run exactly once.");
            });
            await pair.RunTicksSync(5);
            await pair.Client.WaitAssertion(() =>
            {
                var count = CountRadioMessages();
                Assert.That(count - previousCount, Is.EqualTo(stage == 3 ? 0 : 1), $"Delivery stage {stage}");
                previousCount = count;
            });
        }

        await pair.Server.WaitAssertion(() =>
        {
            pair.Server.PlayerMan.SetAttachedEntity(pair.Player!, null);
            entities.DeleteEntity(host);
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();

        int CountRadioMessages()
        {
            var chat = pair.Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
            var count = 0;
            foreach (var (_, message) in chat.History)
            {
                if (message.Channel == ChatChannel.Radio && message.Message.StartsWith("allied headset test ", StringComparison.Ordinal))
                    count++;
            }

            return count;
        }
    }

    private static async Task WithRadioScene(Action<RadioScene> test)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { DummyTicker = false });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.EntMan;
            var wars = entities.System<FactionWarSystem>();
            EntityUid? host = null;
            if (!wars.TryGetState(out var state))
            {
                host = entities.Spawn();
                entities.AddComponent<StationSectorServiceHostComponent>(host.Value);
                Assert.That(wars.TryGetState(out state), Is.True);
            }

            var oldFactions = state.Comp.Factions;
            var oldAlliances = state.Comp.Alliances;
            var oldOffers = state.Comp.AllianceOffers;
            var oldSequence = state.Comp.NextAllianceOfferId;
            var oldWars = state.Comp.Declarations;
            var oldCooldowns = state.Comp.WarCooldowns;
            state.Comp.Factions = ["TSFMC", "PDV", "Khsira", "ExodusRadioTestFaction"];
            state.Comp.Alliances = new();
            state.Comp.AllianceOffers = new();
            state.Comp.Declarations = new();
            state.Comp.WarCooldowns = new();
            try
            {
                var source = entities.SpawnEntity(null, map.GridCoords);
                entities.AddComponent<TelecomExemptComponent>(source);
                var scene = new RadioScene(entities, pair.Server.ProtoMan, wars, state, source, map.GridCoords);
                test(scene);
            }
            finally
            {
                state.Comp.Factions = oldFactions;
                state.Comp.Alliances = oldAlliances;
                state.Comp.AllianceOffers = oldOffers;
                state.Comp.NextAllianceOfferId = oldSequence;
                state.Comp.Declarations = oldWars;
                state.Comp.WarCooldowns = oldCooldowns;
                if (host is { } hostUid)
                    entities.DeleteEntity(hostUid);
            }
        });
        await pair.RunTicksSync(1);
        await pair.CleanReturnAsync();
    }

    private sealed class RadioScene(
        IEntityManager entities,
        IPrototypeManager prototypes,
        FactionWarSystem wars,
        Entity<WarLevelComponent> state,
        EntityUid source,
        EntityCoordinates coordinates)
    {
        public IEntityManager Entities { get; } = entities;
        public IPrototypeManager Prototypes { get; } = prototypes;
        public FactionWarSystem Wars { get; } = wars;
        public Entity<WarLevelComponent> State { get; } = state;
        public EntityUid Source { get; } = source;
        public EntityCoordinates Coordinates { get; } = coordinates;
        private readonly List<Entity<AllianceRadioTestReceiverComponent>> _receivers = new();

        public EntityUid AddReceiver(params ProtoId<RadioChannelPrototype>[] channels)
        {
            var uid = Entities.SpawnEntity(null, Coordinates);
            var radio = Entities.AddComponent<ActiveRadioComponent>(uid);
            foreach (var channel in channels)
                radio.Channels.Add(channel);

            _receivers.Add((uid, Entities.AddComponent<AllianceRadioTestReceiverComponent>(uid)));
            return uid;
        }

        public void Ally(ProtoId<TerritoryFactionPrototype> first, ProtoId<TerritoryFactionPrototype> second)
        {
            Assert.That(Wars.OfferAlliance(first, second), Is.EqualTo(AllianceOfferResult.Success));
            Assert.That(Wars.TryGetAllianceEntry(State, first, second, out var entry), Is.True);
            Assert.That(Wars.AcceptAlliance(second, first, entry.Offer!.Id), Is.EqualTo(AllianceOfferResult.Success));
        }

        public void Tune(EntityUid receiver, int frequency)
        {
            var message = new SelectHandheldRadioFrequencyMessage(frequency) { Actor = Source };
            Entities.EventBus.RaiseLocalEvent(receiver, message);
        }

        public void AssertRecipients(ProtoId<RadioChannelPrototype> channel, List<EntityUid> expected, int? frequency = null)
        {
            foreach (var receiver in _receivers)
                receiver.Comp.Count = 0;

            Entities.System<RadioSystem>().SendRadioMessage(Source, "alliance radio test", channel, Source, frequency);
            foreach (var receiver in _receivers)
            {
                var shouldReceive = expected.Contains(receiver.Owner);
                Assert.That(receiver.Comp.Count, Is.EqualTo(shouldReceive ? 1 : 0), $"Channel {channel}, receiver {receiver.Owner}");
                if (shouldReceive)
                    Assert.That(receiver.Comp.Channel, Is.EqualTo(channel.Id), "The original channel must remain visible.");
            }
        }
    }
}

[RegisterComponent]
public sealed partial class AllianceRadioTestReceiverComponent : Component
{
    public int Count;
    public int HeadsetCount;
    public string Channel;
}

public sealed class AllianceRadioTestReceiverSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AllianceRadioTestReceiverComponent, RadioReceiveEvent>(OnReceive);
        SubscribeLocalEvent<AllianceRadioTestReceiverComponent, RadioMessageHeardEvent>(OnHeadsetReceive);
    }

    private void OnReceive(Entity<AllianceRadioTestReceiverComponent> ent, ref RadioReceiveEvent args)
    {
        ent.Comp.Count++;
        ent.Comp.Channel = args.Channel.ID;
    }

    private void OnHeadsetReceive(Entity<AllianceRadioTestReceiverComponent> ent, ref RadioMessageHeardEvent args)
    {
        ent.Comp.HeadsetCount++;
    }
}
