using System.Numerics;
using Content.Server._Exodus.MedicalTracking;
using Content.Shared._Exodus.MedicalTracking;
using Content.Shared.Implants;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(MedicalTrackingSystem))]
public sealed class MedicalTrackingHudTest
{
    [TestCase("Platinum")]
    [TestCase("Ruby")]
    public async Task UpgradesKeepTheNewBorderAndExtractionRevokesIt(string rejectedTier)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid map = default;
        EntityUid patient = default;
        EntityUid ruby = default;
        EntityUid brain = default;
        EntityUid? rejected = null;

        try
        {
            await server.WaitAssertion(() =>
            {
                map = entities.System<SharedMapSystem>().CreateMap(out _);
                patient = entities.SpawnEntity("MobHuman", new EntityCoordinates(map, Vector2.Zero));
                var implants = entities.System<SharedSubdermalImplantSystem>();
                implants.AddImplant(patient, "MedicalTrackingImplantBasic");
                Assert.That(entities.HasComponent<MedicalTrackingHudComponent>(patient), Is.False);

                foreach (var tier in new[] { "Silver", "Gold", "Platinum", "Ruby" })
                {
                    var implant = implants.AddImplant(patient, $"MedicalTrackingImplant{tier}");
                    Assert.That(implant, Is.Not.Null);
                    Assert.That(entities.GetComponent<MedicalTrackingBodyComponent>(patient).Implant, Is.EqualTo(implant));
                    Assert.That(entities.GetComponent<MedicalTrackingHudComponent>(patient).Border?.Id,
                        Is.EqualTo($"MedicalTrackingBorder{tier}"));
                }

                ruby = entities.GetComponent<MedicalTrackingBodyComponent>(patient).Implant;
                var tracking = entities.GetComponent<MedicalTrackingImplantComponent>(ruby);
                Assert.That(tracking.TrackBody, Is.True);
                Assert.That(tracking.TrackBrain, Is.True);
                Assert.That(tracking.Brain, Is.Not.Null);
                brain = tracking.Brain!.Value;
                Assert.That(entities.GetComponent<MedicalTrackingBrainComponent>(brain).Registered, Is.True);

                // A failed downgrade or duplicate must not clear the currently installed implant's border.
                rejected = implants.AddImplant(patient, $"MedicalTrackingImplant{rejectedTier}");
                Assert.That(rejected, Is.Not.Null);
                Assert.That(entities.GetComponent<MedicalTrackingBodyComponent>(patient).Implant, Is.EqualTo(ruby));
            });

            // Let replaced implants finish shutting down; their cleanup must not revoke the new registration.
            await server.WaitRunTicks(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(entities.EntityExists(rejected!.Value), Is.False);
                Assert.That(entities.GetComponent<MedicalTrackingBodyComponent>(patient).Implant, Is.EqualTo(ruby));
                Assert.That(entities.GetComponent<MedicalTrackingHudComponent>(patient).Border?.Id,
                    Is.EqualTo("MedicalTrackingBorderRuby"));
                Assert.That(entities.GetComponent<MedicalTrackingBrainComponent>(brain).Registered, Is.True);
                Assert.That(entities.GetComponent<MedicalTrackingBrainComponent>(brain).Implant, Is.EqualTo(ruby));
                entities.System<SharedSubdermalImplantSystem>().ForceRemove(patient, ruby);
            });

            await server.WaitRunTicks(2);
            await server.WaitAssertion(() =>
            {
                Assert.That(entities.HasComponent<MedicalTrackingHudComponent>(patient), Is.False);
                Assert.That(entities.GetComponent<MedicalTrackingBrainComponent>(brain).Registered, Is.False);
            });
        }
        finally
        {
            await server.WaitPost(() =>
            {
                if (entities.EntityExists(map))
                    entities.DeleteEntity(map);
            });
        }

        await pair.CleanReturnAsync();
    }
}
