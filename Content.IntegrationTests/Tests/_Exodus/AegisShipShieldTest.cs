using System.Numerics;
using Content.Server._Crescent.ShipShields;
using Content.Server._Mono.Radar;
using Content.Server.Construction.Components;
using Content.Server.Emp;
using Content.Server.Power.EntitySystems;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.ShipShields;
using Content.Shared.Construction.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Power.Components;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
[TestOf(typeof(ShipShieldsSystem))]
public sealed class AegisShipShieldTest
{
    [TestCase("ShieldGeneratorAegis", "ShipShieldHoneycomb")]
    [TestCase("ShieldGeneratorUnsa", "ShipShieldRipple")]
    public async Task FullFieldReplicatesItsSelectedShader(string prototype, string expectedShader)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid generator = default;
        EntityUid field = default;
        var rippleWidth = 0f;
        var rippleSpeed = 0f;

        await server.WaitAssertion(() => generator = SpawnGenerator(em, map.GridCoords, prototype));
        await PoolManager.WaitUntil(server, () => em.GetComponent<ShipShieldEmitterComponent>(generator).Shield != null);
        await server.WaitAssertion(() =>
        {
            var emitter = em.GetComponent<ShipShieldEmitterComponent>(generator);
            field = emitter.Shield!.Value;
            rippleWidth = emitter.RippleWidth;
            rippleSpeed = emitter.RippleSpeed;
            var chain = (ChainShape) em.GetComponent<FixturesComponent>(field).Fixtures["shield"].Shape;

            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<DirectionalShipShieldEmitterComponent>(generator), Is.False);
                Assert.That(em.HasComponent<DirectionalShipShieldFieldComponent>(field), Is.False);
                Assert.That(chain.Vertices[0], Is.EqualTo(chain.Vertices[^1]));
                Assert.That(rippleWidth, Is.Positive);
                Assert.That(rippleSpeed, Is.Positive);
                Assert.That(em.GetComponent<ShipShieldVisualsComponent>(field).RippleShader, Is.EqualTo(expectedShader));
            });
        });

        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            var visuals = pair.Client.EntMan.GetComponent<ShipShieldVisualsComponent>(pair.ToClientUid(field));
            Assert.Multiple(() =>
            {
                Assert.That(visuals.RippleShader, Is.EqualTo(expectedShader));
                Assert.That(visuals.RippleWidth, Is.EqualTo(rippleWidth));
                Assert.That(visuals.RippleSpeed, Is.EqualTo(rippleSpeed));
            });

            var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
            ProtoId<ShaderPrototype> shaderId = visuals.RippleShader;
            using var shader = prototypes.Index(shaderId).InstanceUnique();
            shader.SetParameter("waveSpeed", visuals.RippleSpeed);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("ShieldGeneratorAegis", false)]
    [TestCase("ShieldGeneratorUnsa", true)]
    public async Task EmergencyReserveMatchesGeneratorConfiguration(string prototype, bool acceptsReserve)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid generator = default;
        EntityUid cartridge = default;

        await server.WaitAssertion(() => generator = SpawnGenerator(em, map.GridCoords, prototype));
        await PoolManager.WaitUntil(server, () => em.GetComponent<ShipShieldEmitterComponent>(generator).Shield != null);
        await server.WaitAssertion(() =>
        {
            var emitter = em.GetComponent<ShipShieldEmitterComponent>(generator);
            var slots = em.System<ItemSlotsSystem>();
            var slotId = CdmShieldReserveComponent.GetSlotId(0);
            Assert.Multiple(() =>
            {
                Assert.That(em.HasComponent<CdmShieldReserveComponent>(generator), Is.EqualTo(acceptsReserve));
                Assert.That(slots.TryGetSlot(generator, slotId, out _), Is.EqualTo(acceptsReserve));
                Assert.That(em.System<SharedContainerSystem>().TryGetContainer(generator, slotId, out _),
                    Is.EqualTo(acceptsReserve));
            });

            if (acceptsReserve)
            {
                var reserve = em.GetComponent<CdmShieldReserveComponent>(generator);
                Assert.That(reserve.MaxCartridges, Is.EqualTo(1));
                Assert.That(slots.TryGetSlot(generator, CdmShieldReserveComponent.GetSlotId(1), out _), Is.False);
                cartridge = em.SpawnEntity("CdmShieldReserveCartridge", map.GridCoords);
                Assert.That(slots.TryInsert(generator, slotId, cartridge, null), Is.True);
            }

            var batteryUid = em.SpawnEntity(null, map.GridCoords);
            var battery = em.AddComponent<BatteryComponent>(batteryUid);
            var batteries = em.System<BatterySystem>();
            batteries.SetMaxCharge(batteryUid, 1000f, battery);
            batteries.SetCharge(batteryUid, 1000f, battery);
            var emp = em.System<EmpSystem>();
            Assert.That(emp.TryEmpEffects(batteryUid, 100f, TimeSpan.FromSeconds(5)), Is.False);

            var firstHit = new ShipShieldHitAttemptEvent(map.MapCoords, emitter.MaxDraw + 1f, false);
            em.EventBus.RaiseLocalEvent(map.Grid, ref firstHit);
            Assert.Multiple(() =>
            {
                Assert.That(firstHit.Absorbed, Is.True);
                Assert.That(emp.TryEmpEffects(batteryUid, 100f, TimeSpan.FromSeconds(5)), Is.EqualTo(!acceptsReserve));
                Assert.That(battery.CurrentCharge, Is.EqualTo(acceptsReserve ? 1000f : 900f));
            });

            if (!acceptsReserve)
            {
                Assert.That(emitter.OverloadAccumulator, Is.Positive);
                return;
            }

            Assert.Multiple(() =>
            {
                Assert.That(em.IsQueuedForDeletion(cartridge), Is.True);
                Assert.That(emitter.OverloadAccumulator, Is.Zero);
                Assert.That(emitter.Damage, Is.LessThan(ShipShieldsSystem.CalculateDamageOverloadThreshold(emitter)));
            });

            // The spent cartridge cannot save a second hit, even before its queued deletion runs.
            var secondHit = new ShipShieldHitAttemptEvent(map.MapCoords, emitter.MaxDraw + 1f, false);
            em.EventBus.RaiseLocalEvent(map.Grid, ref secondHit);
            Assert.Multiple(() =>
            {
                Assert.That(secondHit.Absorbed, Is.True);
                Assert.That(emitter.OverloadAccumulator, Is.Positive);
                Assert.That(emp.TryEmpEffects(batteryUid, 100f, TimeSpan.FromSeconds(5)), Is.True);
                Assert.That(battery.CurrentCharge, Is.EqualTo(900f));
            });
        });
        await PoolManager.WaitUntil(server, () => em.GetComponent<ShipShieldEmitterComponent>(generator).Shield == null);
        if (acceptsReserve)
            await server.WaitAssertion(() => Assert.That(em.Deleted(cartridge), Is.True));
        await pair.CleanReturnAsync();
    }

    [TestCase(0)]
    [TestCase(90)]
    [TestCase(180)]
    [TestCase(270)]
    public async Task ThreeTileMachineStaysCenteredWithMatchingBoardAndMs250Performance(int degrees)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var em = pair.Server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid generator = default;
        await pair.Server.WaitAssertion(() =>
        {
            generator = em.SpawnEntity("ShieldGeneratorAegis", map.GridCoords);
            var frame = em.SpawnEntity("MachineFrame3x3Centered", map.GridCoords);
            var transform = em.System<SharedTransformSystem>();
            transform.SetLocalRotation(generator, Angle.FromDegrees(degrees));
            transform.SetLocalRotation(frame, Angle.FromDegrees(degrees));
            var position = transform.GetWorldPosition(generator);
            var physics = em.System<SharedPhysicsSystem>();
            var bounds = physics.GetWorldAABB(generator);
            var frameBounds = physics.GetWorldAABB(frame);
            var blip = em.GetComponent<RadarBlipComponent>(generator);
            var radarBounds = (blip.GridConfig ?? blip.Config).Bounds;
            var machine = em.GetComponent<MachineComponent>(generator);
            Assert.That(machine.BoardContainer.ContainedEntities.Count, Is.EqualTo(1));
            var board = em.GetComponent<MachineBoardComponent>(machine.BoardContainer.ContainedEntities[0]);
            var referenceUid = em.Spawn("ShieldGeneratorMedium");
            var reference = em.GetComponent<ShipShieldEmitterComponent>(referenceUid);
            var emitter = em.GetComponent<ShipShieldEmitterComponent>(generator);

            Assert.Multiple(() =>
            {
                Assert.That(bounds.Width, Is.InRange(2.5f, 3f));
                Assert.That(bounds.Height, Is.InRange(2.5f, 3f));
                Assert.That(bounds.Center.X, Is.EqualTo(position.X).Within(0.0001f));
                Assert.That(bounds.Center.Y, Is.EqualTo(position.Y).Within(0.0001f));
                Assert.That(bounds.Center.X, Is.EqualTo(frameBounds.Center.X).Within(0.0001f));
                Assert.That(bounds.Center.Y, Is.EqualTo(frameBounds.Center.Y).Within(0.0001f));
                Assert.That(radarBounds.Center, Is.EqualTo(Vector2.Zero));
                Assert.That(radarBounds.Width, Is.EqualTo(3f));
                Assert.That(radarBounds.Height, Is.EqualTo(3f));
                Assert.That(board.Prototype.Id, Is.EqualTo("ShieldGeneratorAegis"));
                Assert.That(board.FrameSize, Is.EqualTo(em.GetComponent<MachineFrameComponent>(frame).FrameSize));
                Assert.That(em.GetComponent<ConstructionComponent>(generator).Graph,
                    Is.EqualTo(em.GetComponent<ConstructionComponent>(frame).Graph));
                Assert.That(ShipShieldsSystem.CalculateDamageOverloadThreshold(emitter),
                    Is.InRange(ShipShieldsSystem.CalculateDamageOverloadThreshold(reference) * 0.9f,
                        ShipShieldsSystem.CalculateDamageOverloadThreshold(reference) * 0.99f));
                Assert.That(emitter.DamageLimit, Is.InRange(reference.DamageLimit * 0.9f, reference.DamageLimit * 0.99f));
                Assert.That(emitter.HealPerSecond, Is.InRange(reference.HealPerSecond * 0.9f, reference.HealPerSecond * 0.99f));
                Assert.That(emitter.BaseDraw, Is.EqualTo(reference.BaseDraw));
                Assert.That(emitter.MaxDraw, Is.InRange(reference.MaxDraw * 0.9f, reference.MaxDraw * 0.99f));
                Assert.That(emitter.DamageExp, Is.EqualTo(reference.DamageExp));
                Assert.That(emitter.PowerModifier, Is.EqualTo(reference.PowerModifier));
                Assert.That(emitter.UnpoweredBonus, Is.EqualTo(reference.UnpoweredBonus));
                Assert.That(emitter.DamageOverloadTimePunishment,
                    Is.InRange(reference.DamageOverloadTimePunishment * 1.01f, reference.DamageOverloadTimePunishment * 1.15f));
                Assert.That(emitter.CollisionResistanceMultiplier,
                    Is.InRange(reference.CollisionResistanceMultiplier * 1.01f, reference.CollisionResistanceMultiplier * 1.1f));
                Assert.That(emitter.EmpProtection, Is.True);
            });

            em.DeleteEntity(referenceUid);
            em.DeleteEntity(frame);
        });
        await pair.RunTicksSync(10);
        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client.EntMan;
            var uid = pair.ToClientUid(generator);
            var transform = client.System<SharedTransformSystem>();
            var position = transform.GetWorldPosition(uid);
            var rotation = transform.GetWorldRotation(uid);
            var sprite = client.GetComponent<SpriteComponent>(uid);
            var bounds = client.System<SpriteSystem>()
                .CalculateBounds((uid, sprite), position, rotation, Angle.Zero)
                .CalcBoundingBox();

            Assert.Multiple(() =>
            {
                Assert.That(bounds.Center.X, Is.EqualTo(position.X).Within(0.0001f));
                Assert.That(bounds.Center.Y, Is.EqualTo(position.Y).Within(0.0001f));
                Assert.That(bounds.Width, Is.EqualTo(3f).Within(0.0001f));
                Assert.That(bounds.Height, Is.EqualTo(3f).Within(0.0001f));
                Assert.That(client.GetComponent<PointLightComponent>(uid).Offset, Is.EqualTo(Vector2.Zero));
            });
        });
        await pair.CleanReturnAsync();
    }

    private static EntityUid SpawnGenerator(IEntityManager em, EntityCoordinates coordinates, EntProtoId prototype)
    {
        var generator = em.SpawnEntity(prototype, coordinates);
        em.System<PowerReceiverSystem>().SetNeedsPower(generator, false);
        return generator;
    }
}
