using System.Numerics;
using Content.Server._Exodus.Cartridges;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Materials;
using Content.Shared.Power;
using Content.Shared.Tag;
using Content.Shared.Timing;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus;

public sealed partial class StealthDisruptorTest
{
    [Test]
    public async Task CartridgesAreConsumedReloadedAndRecycled()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        EntityUid map = default;
        EntityUid user = default;
        EntityUid remote = default;
        EntityUid initialCartridge = default;
        EntityUid replacement = default;
        EntityUid spent = default;
        await server.WaitAssertion(() =>
        {
            map = entities.System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, Vector2.Zero);
            user = entities.SpawnEntity("MobHuman", coordinates);
            remote = entities.SpawnEntity("StealthDisruptor", coordinates);
            var slots = entities.System<ItemSlotsSystem>();
            var hands = entities.System<SharedHandsSystem>();
            Assert.That(hands.TryPickupAnyHand(user, remote), Is.True);
            var cartridge = slots.GetItemOrNull(remote, "cartridge");
            Assert.That(cartridge, Is.Not.Null, "The purchased device must start loaded.");
            initialCartridge = cartridge!.Value;
            Assert.That(entities.HasComponent<DisposableCartridgeComponent>(initialCartridge), Is.True);

            var use = new UseInHandEvent(user);
            entities.EventBus.RaiseLocalEvent(remote, use);
            Assert.That(use.Handled, Is.True, "A pulse consumes a cartridge even with no cloaked targets.");
            var casing = slots.GetItemOrNull(remote, "cartridge");
            Assert.That(casing, Is.Not.Null);
            Assert.That(casing, Is.Not.EqualTo(initialCartridge));
            Assert.That(entities.HasComponent<DisposableCartridgeComponent>(casing!.Value), Is.False);
            var tags = entities.System<TagSystem>();
            Assert.That(tags.HasTag(casing.Value, "Trash"), Is.True);
            Assert.That(tags.HasTag(casing.Value, "Recyclable"), Is.True);

            replacement = entities.SpawnEntity("StealthDisruptorCartridge", coordinates);
            Assert.That(hands.TryPickupAnyHand(user, replacement), Is.True);
            var reload = new InteractUsingEvent(user, replacement, remote, coordinates);
            entities.EventBus.RaiseLocalEvent(remote, reload);
            Assert.That(reload.Handled, Is.True);
            Assert.That(slots.GetItemOrNull(remote, "cartridge"), Is.EqualTo(replacement));
            Assert.That(hands.IsHolding(user, casing.Value), Is.True, "Reloading returns the spent casing to the hand.");

            var delay = entities.System<UseDelaySystem>();
            Assert.That(delay.TryGetDelayInfo(remote, out var info), Is.True);
            var available = info!.EndTime;
            use = new UseInHandEvent(user);
            entities.EventBus.RaiseLocalEvent(remote, use);
            Assert.That(use.Handled, Is.False);
            Assert.That(slots.GetItemOrNull(remote, "cartridge"), Is.EqualTo(replacement),
                "Using during cooldown must not consume the new cartridge.");
            Assert.That(info.EndTime, Is.EqualTo(available));
            delay.CancelDelay((remote, entities.GetComponent<UseDelayComponent>(remote)));
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(initialCartridge), Is.True);
            var use = new UseInHandEvent(user);
            entities.EventBus.RaiseLocalEvent(remote, use);
            Assert.That(use.Handled, Is.True);
            var casing = entities.System<ItemSlotsSystem>().GetItemOrNull(remote, "cartridge");
            Assert.That(casing, Is.Not.Null);
            spent = casing!.Value;
            Assert.That(spent, Is.Not.EqualTo(replacement));
            entities.System<UseDelaySystem>().CancelDelay((remote, entities.GetComponent<UseDelayComponent>(remote)));
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(replacement), Is.True);
            var slots = entities.System<ItemSlotsSystem>();
            var delay = entities.System<UseDelaySystem>();
            var use = new UseInHandEvent(user);
            entities.EventBus.RaiseLocalEvent(remote, use);
            Assert.That(use.Handled, Is.False, "A spent cartridge cannot power a second pulse.");
            Assert.That(delay.IsDelayed(remote), Is.False);
            Assert.That(slots.GetItemOrNull(remote, "cartridge"), Is.EqualTo(spent));
            Assert.That(slots.TryEject(remote, "cartridge", null, out _), Is.True);
            use = new UseInHandEvent(user);
            entities.EventBus.RaiseLocalEvent(remote, use);
            Assert.That(use.Handled, Is.False);
            Assert.That(delay.IsDelayed(remote), Is.False, "An empty device must not start its cooldown.");
            var wrongItem = entities.SpawnEntity("PowerCellHigh", new EntityCoordinates(map, Vector2.Zero));
            Assert.That(slots.TryInsert(remote, "cartridge", wrongItem, user), Is.False);

            var recycler = entities.SpawnEntity("Recycler", new EntityCoordinates(map, 2, 0));
            var power = new PowerChangedEvent(true, 500);
            entities.EventBus.RaiseLocalEvent(recycler, ref power);
            var reclaimer = entities.System<SharedMaterialReclaimerSystem>();
            Assert.That(reclaimer.SetReclaimerEnabled(recycler, true), Is.True);
            Assert.That(reclaimer.TryStartProcessItem(recycler, spent), Is.True);
            Assert.That(entities.GetComponent<MaterialReclaimerComponent>(recycler).ItemsProcessed, Is.EqualTo(1));
            var materials = entities.GetComponent<MaterialStorageComponent>(recycler).Storage;
            Assert.That(materials["Steel"], Is.GreaterThan(0));
            Assert.That(materials["Plastic"], Is.GreaterThan(0));
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.Deleted(spent), Is.True);
            entities.DeleteEntity(map);
        });
        await server.WaitRunTicks(2);
        await pair.CleanReturnAsync();
    }
}
