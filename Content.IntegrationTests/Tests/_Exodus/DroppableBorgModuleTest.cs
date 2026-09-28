using Content.Shared._NF.Interaction.Components;
using Content.Shared._NF.Silicons.Borgs;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._Exodus;

[TestFixture]
public sealed class DroppableBorgModuleTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task DeletingSelectedModuleHandlesAlreadyDeletedHandItems(bool deleteItemsFirst)
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var em = server.EntMan;
        var map = await pair.CreateTestMap();
        EntityUid item = default;
        EntityUid placeholder = default;
        await server.WaitAssertion(() =>
        {
            var chassis = em.SpawnEntity("XenoborgChassisStealth", map.GridCoords);
            var borg = em.GetComponent<BorgChassisComponent>(chassis);
            Assert.That(borg.SelectedModule, Is.Not.Null);
            var module = borg.SelectedModule!.Value;
            Assert.That(em.HasComponent<DroppableBorgModuleComponent>(module), Is.True);

            var hands = em.GetComponent<HandsComponent>(chassis);
            string handId = null;
            foreach (var (id, hand) in hands.Hands)
            {
                if (hand.HeldEntity is not { } held
                    || !em.TryGetComponent<HandPlaceholderRemoveableComponent>(held, out var removable))
                    continue;

                item = held;
                placeholder = removable.Placeholder;
                handId = id;
                break;
            }

            Assert.That(handId, Is.Not.Null);
            if (deleteItemsFirst)
            {
                em.DeleteEntity(placeholder);
                em.DeleteEntity(item);
            }

            em.DeleteEntity(module);
            Assert.That(em.System<SharedHandsSystem>().TryGetHand(chassis, handId!, out _, hands), Is.False);
            Assert.That(borg.SelectedModule, Is.Null);
        });
        await server.WaitRunTicks(1);
        await server.WaitAssertion(() =>
        {
            Assert.That(em.Deleted(item), Is.True);
            Assert.That(em.Deleted(placeholder), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}
