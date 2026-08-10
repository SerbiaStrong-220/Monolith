using Content.Shared.Clothing;
using Content.Shared.Gravity;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus.Gravity;

[TestFixture]
[TestOf(typeof(SharedMagbootsSystem))]
public sealed class IntrinsicMagbootsTests
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: IntrinsicMagbootsDummy
  components:
  - type: Physics
    bodyType: Dynamic
  - type: GravityAffected
  - type: ItemToggle
  - type: Magboots
";

    [Test]
    public async Task TogglingRefreshesWeightlessness()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entityManager = server.EntMan;
        var toggleSystem = entityManager.System<ItemToggleSystem>();
        var testMap = await pair.CreateTestMap();

        EntityUid magboots = default;

        await server.WaitAssertion(() =>
        {
            magboots = entityManager.SpawnEntity("IntrinsicMagbootsDummy", testMap.GridCoords);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var gravity = entityManager.GetComponent<GravityAffectedComponent>(magboots);
            var toggle = entityManager.GetComponent<ItemToggleComponent>(magboots);

            Assert.That(gravity.Weightless, Is.True);
            Assert.That(toggleSystem.TryActivate(magboots, predicted: false), Is.True);
            Assert.That(toggle.Activated, Is.True);
            Assert.That(gravity.Weightless, Is.False);

            Assert.That(toggleSystem.TryDeactivate(magboots, predicted: false), Is.True);
            Assert.That(toggle.Activated, Is.False);
            Assert.That(gravity.Weightless, Is.True);
        });

        await pair.CleanReturnAsync();
    }
}
