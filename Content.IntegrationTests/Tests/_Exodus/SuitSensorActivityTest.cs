using System.Numerics;
using Content.Server.Medical.SuitSensors;
using Content.Shared.Emp;
using Content.Shared.Inventory;
using Content.Shared.Medical.SuitSensor;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(SuitSensorSystem))]
public sealed class SuitSensorActivityTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PollingFollowsCarrierModeAndEmpRecovery(bool useContainer)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap(out _);
            try
            {
                var coordinates = new EntityCoordinates(map, Vector2.Zero);
                var patient = entities.SpawnEntity("MobHuman", coordinates);
                var uniform = entities.SpawnEntity("ClothingUniformJumpsuitColorGrey", coordinates);
                var sensor = entities.GetComponent<SuitSensorComponent>(uniform);
                var sensors = entities.System<SuitSensorSystem>();
                var inventory = entities.System<InventorySystem>();
                var containers = entities.System<SharedContainerSystem>();
                var now = server.ResolveDependency<IGameTiming>().CurTime;
                var container = useContainer
                    ? containers.EnsureContainer<ContainerSlot>(patient, "sensor-test")
                    : null;
                if (container != null)
                {
#pragma warning disable RA0002 // Configure the activation container for this test fixture.
                    sensor.ActivationContainer = container.ID;
#pragma warning restore RA0002
                }

                void Attach()
                {
                    Assert.That(container != null
                        ? containers.Insert(uniform, container)
                        : inventory.TryEquip(patient, uniform, "jumpsuit"), Is.True);
                }

                void AssertPolling(bool expected)
                {
                    // Make the sensor due; inactive sensors must not even advance their timer.
#pragma warning disable RA0002 // Force a due update to test multiple transitions within the same tick.
                    sensor.NextUpdate = now;
#pragma warning restore RA0002
                    sensors.Update(0f);
                    var nextUpdate = expected ? now + sensor.UpdateRate : now;
                    Assert.That(sensor.NextUpdate, Is.EqualTo(nextUpdate));
                    sensors.Update(0f);
                    Assert.That(sensor.NextUpdate, Is.EqualTo(nextUpdate), "Keep the transmission interval.");
                }

                sensors.SetSensor((uniform, sensor), SuitSensorMode.SensorCords);
                AssertPolling(false);
                Attach();
                AssertPolling(true);

                sensors.SetSensor((uniform, sensor), SuitSensorMode.SensorOff);
                AssertPolling(false);
                // All transitions occur within one tick, before deferred components are culled.
                foreach (var mode in new[] { SuitSensorMode.SensorBinary, SuitSensorMode.SensorVitals, SuitSensorMode.SensorCords })
                {
                    sensors.SetSensor((uniform, sensor), mode);
                    AssertPolling(true);
                    sensors.SetSensor((uniform, sensor), SuitSensorMode.SensorOff);
                    AssertPolling(false);
                }

                sensors.SetSensor((uniform, sensor), SuitSensorMode.SensorCords);
                var pulse = new EmpPulseEvent(0f, false, false, TimeSpan.FromSeconds(5), null);
                entities.EventBus.RaiseLocalEvent(uniform, ref pulse);
                AssertPolling(false);
                var recovery = new EmpDisabledRemovedEvent();
                entities.EventBus.RaiseLocalEvent(uniform, ref recovery);
                Assert.That(sensor.Mode, Is.EqualTo(SuitSensorMode.SensorCords));
                AssertPolling(true);

                Assert.That(container != null
                    ? containers.Remove(uniform, container)
                    : inventory.TryUnequip(patient, "jumpsuit"), Is.True);
                AssertPolling(false);
                Attach();
                entities.CullRemovedComponents();
                AssertPolling(true);

                entities.System<MobStateSystem>().ChangeMobState(patient, MobState.Dead);
                AssertPolling(true);
                Assert.That(sensors.GetSensorState(uniform, sensor)?.IsAlive, Is.False,
                    "Dead carriers must still be reported.");

                entities.System<MetaDataSystem>().SetEntityPaused(uniform, true);
                AssertPolling(false);
                entities.System<MetaDataSystem>().SetEntityPaused(uniform, false);
                AssertPolling(true);
            }
            finally
            {
                entities.DeleteEntity(map);
            }
        });
        await pair.CleanReturnAsync();
    }
}
