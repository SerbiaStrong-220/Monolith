using Content.Server.Gravity;
using Content.Server.Movement.Systems;
using Content.Shared.Actions;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._Exodus.Movement;

[TestFixture]
[TestOf(typeof(JetpackSystem))]
public sealed class JetpackTests
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: ExodusJetpackTestUser
  components:
  - type: Physics
    bodyType: Dynamic
  - type: GravityAffected
  - type: MovementSpeedModifier
  - type: ContainerContainer

- type: entity
  parent: ExodusJetpackTestUser
  id: ExodusJetpackTestMagbootsUser
  components:
  - type: ItemToggle
  - type: Magboots

- type: entity
  parent: BaseJetpack
  id: ExodusJetpackTestPack
  components:
  - type: Jetpack
    detectionRange: 0
  - type: GasTank
    air:
      volume: 5
      temperature: 293.15
      moles:
        Nitrogen: 1
";

    [Test]
    public async Task GravityPausesFlightWithoutConsumingFuel()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var jetpacks = entities.System<JetpackSystem>();
        var gravity = entities.System<GravitySystem>();
        var map = await pair.CreateTestMap();
        EntityUid user = default;
        Entity<JetpackComponent> pack = default;
        var fuel = 0f;

        await server.WaitAssertion(() =>
        {
            gravity.EnableGravity(map.Grid);
            user = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            pack = SpawnPack(entities, user);
            jetpacks.SetEnabled(pack, true, user);
            AssertEnabled(entities, user, pack, active: false);
            fuel = entities.GetComponent<GasTankComponent>(pack).Air.TotalMoles;
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<GasTankComponent>(pack).Air.TotalMoles, Is.EqualTo(fuel));
            gravity.RefreshGravity(map.Grid);
            AssertEnabled(entities, user, pack, active: true);
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<GasTankComponent>(pack).Air.TotalMoles, Is.LessThan(fuel));
            gravity.EnableGravity(map.Grid);
            AssertEnabled(entities, user, pack, active: false);
            fuel = entities.GetComponent<GasTankComponent>(pack).Air.TotalMoles;
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<GasTankComponent>(pack).Air.TotalMoles, Is.EqualTo(fuel));
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ManuallyDisabledPackDoesNotResumeWhenGravityDisappears()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var gravity = entities.System<GravitySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            gravity.EnableGravity(map.Grid);
            var user = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            var pack = SpawnPack(entities, user);
            var enable = new ToggleJetpackEvent { Performer = user };
            entities.EventBus.RaiseLocalEvent(pack, enable);
            Assert.That(enable.Handled, Is.True);
            AssertEnabled(entities, user, pack, active: false);
            var disable = new ToggleJetpackEvent { Performer = user };
            entities.EventBus.RaiseLocalEvent(pack, disable);
            Assert.That(disable.Handled, Is.True);
            AssertDisabled(entities, user, pack);
            gravity.RefreshGravity(map.Grid);
            AssertDisabled(entities, user, pack);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EnteringGravityGridPausesFlightAndLeavingResumesIt()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var jetpacks = entities.System<JetpackSystem>();
        var gravity = entities.System<GravitySystem>();
        var transforms = entities.System<SharedTransformSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            gravity.EnableGravity(map.Grid);
            var space = new EntityCoordinates(map.MapUid, 50, 50);
            var user = entities.SpawnEntity("ExodusJetpackTestUser", space);
            var pack = SpawnPack(entities, user);
            jetpacks.SetEnabled(pack, true, user);
            AssertEnabled(entities, user, pack, active: true);

            transforms.SetCoordinates(user, map.GridCoords);
            Assert.That(entities.GetComponent<TransformComponent>(user).GridUid, Is.EqualTo(map.Grid.Owner));
            AssertEnabled(entities, user, pack, active: false);

            transforms.SetCoordinates(user, space);
            Assert.That(entities.GetComponent<TransformComponent>(user).GridUid, Is.Null);
            AssertEnabled(entities, user, pack, active: true);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task MagbootsPauseFlightOnlyWhenTheyProvideSupport()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var jetpacks = entities.System<JetpackSystem>();
        var toggle = entities.System<ItemToggleSystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var user = entities.SpawnEntity("ExodusJetpackTestMagbootsUser", map.GridCoords);
            var pack = SpawnPack(entities, user);
            jetpacks.SetEnabled(pack, true, user);
            AssertEnabled(entities, user, pack, active: true);

            Assert.That(toggle.TryActivate(user, predicted: false), Is.True);
            AssertEnabled(entities, user, pack, active: false);
            Assert.That(toggle.TryDeactivate(user, predicted: false), Is.True);
            AssertEnabled(entities, user, pack, active: true);

            Assert.That(toggle.TryActivate(user, predicted: false), Is.True);
            AssertEnabled(entities, user, pack, active: false);
            entities.System<SharedTransformSystem>().SetCoordinates(user, new EntityCoordinates(map.MapUid, 50, 50));
            Assert.That(entities.GetComponent<TransformComponent>(user).GridUid, Is.Null);
            AssertEnabled(entities, user, pack, active: true);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task RemovingPackDisablesItAndPreventsAnotherOwnerFromResuming(bool paused)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var containers = entities.System<SharedContainerSystem>();
        var jetpacks = entities.System<JetpackSystem>();
        var gravity = entities.System<GravitySystem>();
        var map = await pair.CreateTestMap();
        EntityUid user = default;
        EntityUid other = default;
        Entity<JetpackComponent> pack = default;

        await server.WaitAssertion(() =>
        {
            if (paused)
                gravity.EnableGravity(map.Grid);

            user = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            other = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            pack = SpawnPack(entities, user);
            jetpacks.SetEnabled(pack, true, user);
            AssertEnabled(entities, user, pack, active: !paused);
            Assert.That(containers.TryGetContainingContainer((pack.Owner, null, null), out var container), Is.True);
            Assert.That(containers.Remove(pack.Owner, container!), Is.True);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            AssertDisabled(entities, user, pack);
            Assert.That(containers.Insert(pack.Owner, containers.EnsureContainer<Container>(other, "jetpack-test")), Is.True);
            gravity.RefreshGravity(map.Grid);
            AssertDisabled(entities, other, pack);
            Assert.That(entities.HasComponent<JetpackUserComponent>(user), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EnablingAnotherPackReplacesThePreviousOne(bool paused)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var jetpacks = entities.System<JetpackSystem>();
        var gravity = entities.System<GravitySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            if (paused)
                gravity.EnableGravity(map.Grid);

            var user = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            var first = SpawnPack(entities, user);
            var second = SpawnPack(entities, user);
            jetpacks.SetEnabled(first, true, user);
            AssertEnabled(entities, user, first, active: !paused);
            jetpacks.SetEnabled(second, true, user);
            Assert.That(first.Comp.JetpackUser, Is.Null);
            Assert.That(IsActive(entities, first), Is.False);
            AssertActionToggled(entities, first, toggled: false);
            AssertEnabled(entities, user, second, active: !paused);
            gravity.RefreshGravity(map.Grid);
            Assert.That(IsActive(entities, first), Is.False);
            AssertEnabled(entities, user, second, active: true);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TransferringEnabledPackToAnotherOwnerDisablesIt(bool paused)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var containers = entities.System<SharedContainerSystem>();
        var jetpacks = entities.System<JetpackSystem>();
        var gravity = entities.System<GravitySystem>();
        var map = await pair.CreateTestMap();
        EntityUid user = default;
        EntityUid other = default;
        Entity<JetpackComponent> pack = default;

        await server.WaitAssertion(() =>
        {
            if (paused)
                gravity.EnableGravity(map.Grid);

            user = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            other = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            pack = SpawnPack(entities, user);
            jetpacks.SetEnabled(pack, true, user);
            AssertEnabled(entities, user, pack, active: !paused);
            Assert.That(containers.Insert(pack.Owner, containers.EnsureContainer<Container>(other, "jetpack-test")), Is.True);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            AssertDisabled(entities, user, pack);
            Assert.That(entities.HasComponent<JetpackUserComponent>(other), Is.False);
            gravity.RefreshGravity(map.Grid);
            AssertDisabled(entities, other, pack);
        });

        await pair.CleanReturnAsync();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DeletingPackClearsItsOwnersFlightState(bool paused)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var jetpacks = entities.System<JetpackSystem>();
        var gravity = entities.System<GravitySystem>();
        var map = await pair.CreateTestMap();
        EntityUid user = default;

        await server.WaitAssertion(() =>
        {
            if (paused)
                gravity.EnableGravity(map.Grid);

            user = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            var pack = SpawnPack(entities, user);
            jetpacks.SetEnabled(pack, true, user);
            AssertEnabled(entities, user, pack, active: !paused);
            entities.DeleteEntity(pack);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.HasComponent<JetpackUserComponent>(user), Is.False);
            Assert.That(jetpacks.IsUserFlying(user), Is.False);
            gravity.RefreshGravity(map.Grid);
            Assert.That(jetpacks.IsUserFlying(user), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task RemovingFlightComponentDisablesItsAssociatedPack()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var jetpacks = entities.System<JetpackSystem>();
        var map = await pair.CreateTestMap();
        EntityUid user = default;
        Entity<JetpackComponent> pack = default;

        await server.WaitAssertion(() =>
        {
            user = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            pack = SpawnPack(entities, user);
            jetpacks.SetEnabled(pack, true, user);
            AssertEnabled(entities, user, pack, active: true);
            entities.RemoveComponentDeferred<JetpackUserComponent>(user);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            AssertDisabled(entities, user, pack);
            Assert.That(entities.HasComponent<JetpackUserComponent>(user), Is.False);
            Assert.That(entities.HasComponent<ActiveJetpackComponent>(pack), Is.False);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task EmptyPackCannotResumeFromStandby()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var jetpacks = entities.System<JetpackSystem>();
        var gravity = entities.System<GravitySystem>();
        var map = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            gravity.EnableGravity(map.Grid);
            var user = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            var pack = SpawnPack(entities, user);
            jetpacks.SetEnabled(pack, true, user);
            AssertEnabled(entities, user, pack, active: false);
            entities.GetComponent<GasTankComponent>(pack).Air.SetMoles(Gas.Nitrogen, 0);
            gravity.RefreshGravity(map.Grid);
            AssertDisabled(entities, user, pack);
            gravity.EnableGravity(map.Grid);
            gravity.RefreshGravity(map.Grid);
            AssertDisabled(entities, user, pack);
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FuelExhaustionDisablesFlightAndClearsTheOwner()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entities = server.EntMan;
        var jetpacks = entities.System<JetpackSystem>();
        var map = await pair.CreateTestMap();
        EntityUid user = default;
        Entity<JetpackComponent> pack = default;

        await server.WaitAssertion(() =>
        {
            user = entities.SpawnEntity("ExodusJetpackTestUser", map.GridCoords);
            pack = SpawnPack(entities, user);
            entities.GetComponent<GasTankComponent>(pack).Air.SetMoles(Gas.Nitrogen, pack.Comp.MoleUsage);
            jetpacks.SetEnabled(pack, true, user);
            AssertEnabled(entities, user, pack, active: true);
        });

        await server.WaitRunTicks(30);

        await server.WaitAssertion(() =>
        {
            Assert.That(entities.GetComponent<GasTankComponent>(pack).Air.TotalMoles, Is.Zero);
            AssertDisabled(entities, user, pack);
        });

        await pair.CleanReturnAsync();
    }

    private static Entity<JetpackComponent> SpawnPack(IEntityManager entities, EntityUid user)
    {
        var containers = entities.System<SharedContainerSystem>();
        var pack = entities.SpawnEntity("ExodusJetpackTestPack", entities.GetComponent<TransformComponent>(user).Coordinates);
        Assert.That(containers.Insert(pack, containers.EnsureContainer<Container>(user, "jetpack-test")), Is.True);
        return (pack, entities.GetComponent<JetpackComponent>(pack));
    }

    private static void AssertEnabled(IEntityManager entities, EntityUid user, Entity<JetpackComponent> pack, bool active)
    {
        Assert.That(pack.Comp.JetpackUser, Is.EqualTo(user));
        Assert.That(entities.TryGetComponent<JetpackUserComponent>(user, out var owner), Is.True);
        Assert.That(owner!.Jetpack, Is.EqualTo(pack.Owner));
        Assert.That(owner.Active, Is.EqualTo(active));
        Assert.That(IsActive(entities, pack), Is.EqualTo(active));
        Assert.That(entities.System<JetpackSystem>().IsUserFlying(user), Is.EqualTo(active));
        var movement = new CanWeightlessMoveEvent(user);
        entities.EventBus.RaiseLocalEvent(user, ref movement);
        Assert.That(movement.CanMove, Is.EqualTo(active));
        AssertActionToggled(entities, pack, toggled: true);
    }

    private static void AssertDisabled(IEntityManager entities, EntityUid user, Entity<JetpackComponent> pack)
    {
        Assert.That(pack.Comp.JetpackUser, Is.Null);
        Assert.That(IsActive(entities, pack), Is.False);
        Assert.That(entities.TryGetComponent<JetpackUserComponent>(user, out var owner) && owner.Running, Is.False);
        Assert.That(entities.System<JetpackSystem>().IsUserFlying(user), Is.False);
        AssertActionToggled(entities, pack, toggled: false);
    }

    private static void AssertActionToggled(IEntityManager entities, Entity<JetpackComponent> pack, bool toggled)
    {
        var actions = entities.System<SharedActionsSystem>();
        Assert.That(actions.TryGetActionData(pack.Comp.ToggleActionEntity, out var action), Is.True);
        Assert.That(action!.Toggled, Is.EqualTo(toggled));
    }

    private static bool IsActive(IEntityManager entities, Entity<JetpackComponent> pack)
    {
        return entities.TryGetComponent<ActiveJetpackComponent>(pack, out var active) && active.Running;
    }
}
