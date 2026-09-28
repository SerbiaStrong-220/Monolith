using System.Numerics;
using Content.Server._Exodus.MedicalTracking;
using Content.Server.Body.Components;
using Content.Server.Medical.SuitSensors;
using Content.Shared._Exodus.MedicalTracking;
using Content.Shared.Access.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Implants;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Pinpointer;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(MedicalTrackingSystem))]
public sealed class MedicalTrackingVisibilityTest
{
    [TestCase("MedicalTrackingImplantBasic", false)]
    [TestCase("MedicalTrackingImplantSilver", false)]
    [TestCase("MedicalTrackingImplantGold", true)]
    [TestCase("MedicalTrackingImplantPlatinum", true)]
    [TestCase("MedicalTrackingImplantRuby", true)]
    public async Task TabletAlertsFollowTransitionsWithClosedUi(string prototype, bool shouldAlert)
    {
        await WithPatient(prototype, (entities, patient, implant, map, now) =>
        {
            var tracking = entities.System<MedicalTrackingSystem>();
            var states = entities.System<MobStateSystem>();
            var tablet = entities.SpawnEntity("MedicalTrackingTablet", new EntityCoordinates(map, Vector2.Zero));
            var device = entities.GetComponent<MedicalTrackingTabletComponent>(tablet);
            // Neither UI refreshes nor periodic position samples should be needed for an emergency alert.
            device.NextUpdate = now + TimeSpan.FromMinutes(1);
            implant.Comp.NextUpdate = device.NextUpdate;
            Assert.That(entities.System<UserInterfaceSystem>().IsUiOpen(tablet, MedicalTrackingUiKey.Key), Is.False);

            states.ChangeMobState(patient, MobState.Critical);
            tracking.Update(0f);
            Assert.That(CountTabletSounds(entities, tablet, "critical"), Is.EqualTo(shouldAlert ? 1 : 0));
            tracking.Update(0f);
            Assert.That(CountTabletSounds(entities, tablet, "critical"), Is.EqualTo(shouldAlert ? 1 : 0),
                "Remaining critical must not repeat the notification.");

            states.ChangeMobState(patient, MobState.Dead);
            tracking.Update(0f);
            Assert.That(CountTabletSounds(entities, tablet, "death"), Is.Zero, "The cooldown must prevent overlap.");
            device.NextAlert = now;
            tracking.Update(0f);
            Assert.That(CountTabletSounds(entities, tablet, "death"), Is.EqualTo(shouldAlert ? 1 : 0));

            // Reviving into critical is an improvement, not a new critical emergency.
            states.ChangeMobState(patient, MobState.Critical);
            states.ChangeMobState(patient, MobState.Alive);
            device.NextAlert = now;
            tracking.Update(0f);
            Assert.That(device.PendingAlert, Is.Null);
            Assert.That(CountTabletSounds(entities, tablet, "critical"), Is.EqualTo(shouldAlert ? 1 : 0));
        });
    }

    [Test]
    public async Task SimultaneousTabletAlertsPreferDeathAndSkipPausedDevices()
    {
        await WithPatient("MedicalTrackingImplantGold", (entities, patient, _, map, _) =>
        {
            var origin = new EntityCoordinates(map, Vector2.Zero);
            var tracking = entities.System<MedicalTrackingSystem>();
            var states = entities.System<MobStateSystem>();
            var tablet = entities.SpawnEntity("MedicalTrackingTablet", origin);
            var paused = entities.SpawnEntity("MedicalTrackingTablet", origin);
            entities.System<MetaDataSystem>().SetEntityPaused(paused, true);
            var otherPatient = entities.SpawnEntity("MobHuman", origin);
            Assert.That(entities.System<SharedSubdermalImplantSystem>().AddImplant(
                otherPatient, "MedicalTrackingImplantGold"), Is.Not.Null);

            states.ChangeMobState(patient, MobState.Critical);
            states.ChangeMobState(patient, MobState.Dead);
            states.ChangeMobState(otherPatient, MobState.Critical);
            tracking.Update(0f);

            Assert.That(CountTabletSounds(entities, tablet, "death"), Is.EqualTo(1));
            Assert.That(CountTabletSounds(entities, tablet, "critical"), Is.Zero);
            Assert.That(entities.GetComponent<MedicalTrackingTabletComponent>(paused).PendingAlert, Is.Null);
            entities.System<MetaDataSystem>().SetEntityPaused(paused, false);
            tracking.Update(0f);
            Assert.That(CountTabletSounds(entities, paused, "death"), Is.Zero);
            Assert.That(CountTabletSounds(entities, paused, "critical"), Is.Zero);
        });
    }

    private static int CountTabletSounds(IEntityManager entities, EntityUid tablet, string sound)
    {
        var count = 0;
        var query = entities.EntityQueryEnumerator<AudioComponent, TransformComponent>();
        while (query.MoveNext(out _, out var audio, out var transform))
        {
            if (transform.ParentUid != tablet || audio.FileName != $"/Audio/_Exodus/Items/MedicalTablet/{sound}.ogg")
                continue;

            Assert.That(audio.Global, Is.False);
            Assert.That(audio.Params.MaxDistance, Is.EqualTo(5f));
            count++;
        }

        return count;
    }

    [TestCase("MedicalTrackingImplantGold")]
    [TestCase("MedicalTrackingImplantPlatinum")]
    [TestCase("MedicalTrackingImplantRuby")]
    public async Task SuitSensorModesGateLivingClientsButNotEmergencies(string prototype)
    {
        await WithPatient(prototype, (entities, patient, implant, map, now) =>
        {
            var tracking = entities.System<MedicalTrackingSystem>();
            var sensors = entities.System<SuitSensorSystem>();
            var states = entities.System<MobStateSystem>();
            var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", new EntityCoordinates(map, Vector2.Zero));
            Assert.That(entities.System<InventorySystem>().TryEquip(patient, uniform, "jumpsuit"), Is.True);
            var sensor = entities.GetComponent<SuitSensorComponent>(uniform);

            foreach (var mode in Enum.GetValues<SuitSensorMode>())
            {
                sensors.SetSensor((uniform, sensor), mode);
                foreach (var state in new[] { MobState.Alive, MobState.Critical, MobState.Dead })
                {
                    states.ChangeMobState(patient, state);
                    implant.Comp.NextUpdate = now;
                    tracking.Update(0f);
                    var visible = state != MobState.Alive || mode == SuitSensorMode.SensorCords;
                    Assert.That(tracking.GetContacts().Exists(c => c.Body == entities.GetNetEntity(patient)),
                        Is.EqualTo(visible), $"Tablet: {mode}, {state}");
                    Assert.That(implant.Comp.Contact.HasValue, Is.EqualTo(visible));
                    if (implant.Comp.Brain is { } brain)
                    {
                        Assert.That(tracking.GetBrains().Exists(c => c.Entity == entities.GetNetEntity(brain)),
                            Is.EqualTo(visible), $"Pinpointer: {mode}, {state}");
                        Assert.That(entities.GetComponent<MedicalTrackingBrainComponent>(brain).Registered, Is.True);
                    }
                }
            }
        });
    }

    [Test]
    public async Task DisablingSensorsClearsCachedContactsAndSelectedTargetsAndRejectsStaleSelection()
    {
        await WithPatient("MedicalTrackingImplantPlatinum", (entities, patient, implant, map, now) =>
        {
            var tracking = entities.System<MedicalTrackingSystem>();
            var inventory = entities.System<InventorySystem>();
            var sensors = entities.System<SuitSensorSystem>();
            var states = entities.System<MobStateSystem>();
            var ui = entities.System<UserInterfaceSystem>();
            var origin = new EntityCoordinates(map, Vector2.Zero);
            var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", origin);
            var sensor = entities.GetComponent<SuitSensorComponent>(uniform);
            sensors.SetSensor((uniform, sensor), SuitSensorMode.SensorCords);
            var brain = implant.Comp.Brain!.Value;
            var netBrain = entities.GetNetEntity(brain);
            var tablet = entities.SpawnEntity("MedicalTrackingTablet", origin);
            var tabletComp = entities.GetComponent<MedicalTrackingTabletComponent>(tablet);
            var pointer = entities.SpawnEntity("MedicalTrackingPinpointer", origin);
            var device = entities.GetComponent<MedicalTrackingPinpointerComponent>(pointer);
            var pinpointer = entities.GetComponent<PinpointerComponent>(pointer);
            var medic = entities.SpawnEntity("MobHuman", origin);
            // Test privacy filtering independently of the medic's access card.
            entities.RemoveComponent<AccessReaderComponent>(tablet);
            entities.RemoveComponent<AccessReaderComponent>(pointer);
            Assert.That(ui.TryOpenUi(tablet, MedicalTrackingUiKey.Key, medic), Is.True);
            Assert.That(ui.TryOpenUi(pointer, MedicalTrackingUiKey.Pinpointer, medic), Is.True);

            // A sensor on the floor must not reveal its nearby owner.
            Refresh();
            AssertVisible(false);
            Assert.That(inventory.TryEquip(patient, uniform, "jumpsuit"), Is.True);
            Refresh();
            AssertVisible(true);
            SelectBrain();
            Assert.That(pinpointer.Target, Is.EqualTo(brain));
            Assert.That(pinpointer.IsActive, Is.True);

            sensors.SetSensor((uniform, sensor), SuitSensorMode.SensorOff);
            // Keep the last body sample: filtering must hide it before the next five-second sample.
            Refresh(sample: false);
            Assert.That(implant.Comp.Contact, Is.Not.Null);
            AssertVisible(false);
            Assert.That(pinpointer.Target, Is.Null);
            Assert.That(pinpointer.IsActive, Is.False);
            SelectBrain();
            Assert.That(pinpointer.Target, Is.Null, "A previously known brain ID cannot bypass sensor privacy.");

            states.ChangeMobState(patient, MobState.Critical);
            Refresh();
            AssertVisible(true);
            SelectBrain();
            Assert.That(pinpointer.Target, Is.EqualTo(brain));
            states.ChangeMobState(patient, MobState.Alive);
            Refresh(sample: false);
            AssertVisible(false);
            Assert.That(pinpointer.Target, Is.Null);

            sensors.SetSensor((uniform, sensor), SuitSensorMode.SensorCords);
            Refresh();
            AssertVisible(true);
            SelectBrain();
            Assert.That(inventory.TryUnequip(patient, "jumpsuit"), Is.True);
            Refresh(sample: false);
            AssertVisible(false);
            Assert.That(pinpointer.Target, Is.Null);

            // Neither critical nor dead clients need to be wearing a sensor at all.
            states.ChangeMobState(patient, MobState.Critical);
            Refresh();
            AssertVisible(true);
            states.ChangeMobState(patient, MobState.Dead);
            Refresh();
            AssertVisible(true);

            void SelectBrain()
            {
                var message = new MedicalTrackingSelectMessage(netBrain)
                {
                    Actor = medic,
                    UiKey = MedicalTrackingUiKey.Pinpointer,
                };
                entities.EventBus.RaiseLocalEvent(pointer, message);
            }

            void Refresh(bool sample = true)
            {
                if (sample)
                    implant.Comp.NextUpdate = now;
                tabletComp.NextUpdate = now;
                device.NextUpdate = now;
                tracking.Update(0f);
            }

            void AssertVisible(bool visible)
            {
                Assert.That(ui.TryGetUiState<MedicalTrackingState>(tablet, MedicalTrackingUiKey.Key, out var contacts), Is.True);
                Assert.That(contacts!.Contacts.Exists(c => c.Body == entities.GetNetEntity(patient)), Is.EqualTo(visible));
                Assert.That(ui.TryGetUiState<MedicalTrackingPinpointerState>(pointer, MedicalTrackingUiKey.Pinpointer, out var brains), Is.True);
                Assert.That(brains!.Brains.Exists(c => c.Entity == netBrain), Is.EqualTo(visible));
                if (!visible)
                    Assert.That(brains.Target, Is.Null);
            }
        });
    }

    [Test]
    public async Task DetachedBrainRemainsVisibleButTransplantedBrainUsesItsCurrentBodysSensors()
    {
        await WithPatient("MedicalTrackingImplantPlatinum", (entities, patient, implant, map, _) =>
        {
            var tracking = entities.System<MedicalTrackingSystem>();
            var bodies = entities.System<SharedBodySystem>();
            var brain = implant.Comp.Brain!.Value;
            var netBrain = entities.GetNetEntity(brain);
            Assert.That(tracking.GetBrains().Exists(c => c.Entity == netBrain), Is.False);
            Assert.That(bodies.RemoveOrgan(brain), Is.True);
            Assert.That(tracking.GetBrains().Exists(c => c.Entity == netBrain), Is.True);

            var recipient = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.One));
            var organs = bodies.GetBodyOrganEntityComps<BrainComponent>((recipient, entities.GetComponent<BodyComponent>(recipient)));
            Assert.That(organs, Has.Count.EqualTo(1));
            var oldBrain = organs[0].Owner;
            var head = entities.GetComponent<TransformComponent>(oldBrain).ParentUid;
            var slot = entities.GetComponent<OrganComponent>(oldBrain).SlotId;
            Assert.That(bodies.RemoveOrgan(oldBrain), Is.True);
            Assert.That(bodies.InsertOrgan(head, brain, slot), Is.True);
            entities.System<MobStateSystem>().ChangeMobState(recipient, MobState.Alive);
            Assert.That(tracking.GetBrains().Exists(c => c.Entity == netBrain), Is.False);

            var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", new EntityCoordinates(map, Vector2.One));
            Assert.That(entities.System<InventorySystem>().TryEquip(recipient, uniform, "jumpsuit"), Is.True);
            entities.System<SuitSensorSystem>().SetSensor(
                (uniform, entities.GetComponent<SuitSensorComponent>(uniform)), SuitSensorMode.SensorCords);
            Assert.That(tracking.GetBrains().Exists(c => c.Entity == netBrain), Is.True);
        });
    }

    private static async Task WithPatient(string prototype,
        Action<IEntityManager, EntityUid, Entity<MedicalTrackingImplantComponent>, EntityUid, TimeSpan> test)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap(out _);
            try
            {
                var patient = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
                var implant = entities.System<SharedSubdermalImplantSystem>().AddImplant(patient, prototype);
                Assert.That(implant, Is.Not.Null);
                test(entities, patient, (implant!.Value, entities.GetComponent<MedicalTrackingImplantComponent>(implant.Value)),
                    map, server.ResolveDependency<IGameTiming>().CurTime);
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });
        await pair.CleanReturnAsync();
    }
}
