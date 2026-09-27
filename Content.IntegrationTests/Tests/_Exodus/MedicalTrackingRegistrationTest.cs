using System.Numerics;
using Content.Server._Exodus.MedicalTracking;
using Content.Server.Body.Components;
using Content.Server.IdentityManagement;
using Content.Server.Medical.SuitSensors;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Systems;
using Content.Shared.Implants;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(MedicalTrackingSystem))]
public sealed class MedicalTrackingRegistrationTest
{
    [TestCase("MedicalTrackingImplantPlatinum", "medical-tracking-tier-platinum")]
    [TestCase("MedicalTrackingImplantRuby", "medical-tracking-tier-ruby")]
    public async Task SpawnTimeIdentityAndTierReachPinpointerAndSurviveBodyDestruction(string prototype, string tier)
    {
        await WithPatient((entities, patient, map, _) =>
        {
            var metadata = entities.System<MetaDataSystem>();
            var identity = entities.System<IdentitySystem>();
            var tracking = entities.System<MedicalTrackingSystem>();
            metadata.SetEntityName(patient, "Medical Client");

            // Do not process the queued identity update: job specials run in the same tick as spawning.
            var implant = entities.System<SharedSubdermalImplantSystem>().AddImplant(patient, prototype);
            Assert.That(implant, Is.Not.Null);
            var brain = entities.GetComponent<MedicalTrackingImplantComponent>(implant!.Value).Brain;
            Assert.That(brain, Is.Not.Null);
            var netBrain = entities.GetNetEntity(brain!.Value);
            var clients = tracking.GetBrains();
            var client = clients.Find(c => c.Entity == netBrain);
            Assert.That(client.Name, Is.EqualTo("Medical Client"));
            Assert.That(client.TierName, Is.EqualTo(tier));

            metadata.SetEntityName(patient, "Final Spawn Name");
            identity.Update(0f);
            Assert.That(tracking.GetBrains().Find(c => c.Entity == netBrain).Name, Is.EqualTo("Final Spawn Name"));

            Assert.That(entities.System<SharedBodySystem>().RemoveOrgan(brain.Value), Is.True);
            entities.System<SharedTransformSystem>().SetCoordinates(brain.Value, new EntityCoordinates(map, Vector2.One));
            metadata.SetEntityName(patient, "Body After Extraction");
            identity.Update(0f);
            Assert.That(tracking.GetBrains().Find(c => c.Entity == netBrain).Name, Is.EqualTo("Final Spawn Name"));

            entities.DeleteEntity(patient);
            client = tracking.GetBrains().Find(c => c.Entity == netBrain);
            Assert.That(client.Name, Is.EqualTo("Final Spawn Name"));
            Assert.That(client.TierName, Is.EqualTo(tier));
        });
    }

    [TestCase("MedicalTrackingImplantPlatinum")]
    [TestCase("MedicalTrackingImplantRuby")]
    public async Task BrainArrivingAfterJobImplantIsRegistered(string prototype)
    {
        await WithPatient((entities, patient, _, _) =>
        {
            var bodies = entities.System<SharedBodySystem>();
            var organs = bodies.GetBodyOrganEntityComps<BrainComponent>(
                (patient, entities.GetComponent<BodyComponent>(patient)));
            Assert.That(organs, Has.Count.EqualTo(1));
            var brain = organs[0].Owner;
            var part = entities.GetComponent<TransformComponent>(brain).ParentUid;
            var slot = entities.GetComponent<OrganComponent>(brain).SlotId;
            Assert.That(bodies.RemoveOrgan(brain), Is.True);

            // Starting job implants bypass the interactive implantation attempt, even without an initialized brain.
            var implant = entities.System<SharedSubdermalImplantSystem>().AddImplant(patient, prototype);
            Assert.That(implant, Is.Not.Null);
            var config = entities.GetComponent<MedicalTrackingImplantComponent>(implant!.Value);
            Assert.That(config.Brain, Is.Null);
            Assert.That(bodies.InsertOrgan(part, brain, slot), Is.True);
            Assert.That(config.Brain, Is.EqualTo(brain));
            Assert.That(entities.System<MedicalTrackingSystem>().GetBrains().Exists(
                c => c.Entity == entities.GetNetEntity(brain)), Is.True);
        });
    }

    [Test]
    public async Task PeriodicSamplingRepairsAMissingBrainRegistration()
    {
        await WithPatient((entities, patient, _, now) =>
        {
            var implant = entities.System<SharedSubdermalImplantSystem>().AddImplant(patient, "MedicalTrackingImplantRuby");
            Assert.That(implant, Is.Not.Null);
            var config = entities.GetComponent<MedicalTrackingImplantComponent>(implant!.Value);
            Assert.That(config.Brain, Is.Not.Null);
            var brain = config.Brain!.Value;
            entities.RemoveComponent<MedicalTrackingBrainComponent>(brain);
            config.Brain = null;
            config.NextUpdate = now;

            var tracking = entities.System<MedicalTrackingSystem>();
            tracking.Update(0f);
            Assert.That(config.Brain, Is.EqualTo(brain));
            Assert.That(tracking.GetBrains().Exists(c => c.Entity == entities.GetNetEntity(brain)), Is.True);
        });
    }

    private static async Task WithPatient(Action<IEntityManager, EntityUid, EntityUid, TimeSpan> test)
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
                var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", new EntityCoordinates(map, Vector2.Zero));
                Assert.That(entities.System<InventorySystem>().TryEquip(patient, uniform, "jumpsuit"), Is.True);
                entities.System<SuitSensorSystem>().SetSensor(
                    (uniform, entities.GetComponent<SuitSensorComponent>(uniform)), SuitSensorMode.SensorCords);
                test(entities, patient, map, server.ResolveDependency<IGameTiming>().CurTime);
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });
        await pair.CleanReturnAsync();
    }
}
