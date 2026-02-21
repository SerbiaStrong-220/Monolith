// Exodus-MiningScannerRefactor
using System.Linq;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mining.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Content.Shared.Mining;

public sealed partial class MiningScannerSystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MiningScannerViewerSystem _viewer = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<MiningScannerComponent, EntGotInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<MiningScannerComponent, EntGotRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<MiningScannerComponent, ItemToggledEvent>(OnToggled);
    }

    private void OnInserted(Entity<MiningScannerComponent> ent, ref EntGotInsertedIntoContainerMessage args)
    {
        UpdateViewerComponent(args.Container.Owner);
    }

    private void OnRemoved(Entity<MiningScannerComponent> ent, ref EntGotRemovedFromContainerMessage args)
    {
        UpdateViewerComponent(args.Container.Owner);
    }

    private void OnToggled(Entity<MiningScannerComponent> ent, ref ItemToggledEvent args)
    {
        if (_container.TryGetContainingContainer((ent.Owner, null, null), out var container))
            UpdateViewerComponent(container.Owner);
    }

    public void UpdateViewerComponent(EntityUid uid)
    {
        Entity<MiningScannerComponent>? scannerEnt = null;

        var ents = _inventory.GetHandOrInventoryEntities(uid).Append(uid);
        foreach (var ent in ents)
        {
            if (!TryComp<MiningScannerComponent>(ent, out var scannerComponent) ||
                !TryComp<ItemToggleComponent>(ent, out var toggle))
                continue;

            if (!toggle.Activated)
                continue;

            if (scannerEnt == null || scannerComponent.Range > scannerEnt.Value.Comp.Range)
                scannerEnt = (ent, scannerComponent);
        }

        if (scannerEnt == null)
        {
            if (TryComp<MiningScannerUserComponent>(uid, out var scannerUser))
                scannerUser.QueueRemoval = true;
        }
        else
        {
            var scannerUser = EnsureComp<MiningScannerUserComponent>(uid);
            scannerUser.ViewRange = scannerEnt.Value.Comp.Range;
            scannerUser.QueueRemoval = false;
            scannerUser.NextPingTime = _timing.CurTime + scannerUser.PingDelay;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MiningScannerUserComponent>();
        while (query.MoveNext(out var uid, out var scannerUser))
        {
            if (scannerUser.QueueRemoval)
            {
                RemCompDeferred(uid, scannerUser);
                continue;
            }

            if (_timing.CurTime < scannerUser.NextPingTime)
                continue;

            scannerUser.NextPingTime = _timing.CurTime + scannerUser.PingDelay + TimeSpan.FromSeconds(scannerUser.AnimationDuration);
            _viewer.CreateScan(uid, scannerUser.ViewRange, scannerUser.PingDelay, scannerUser.AnimationDuration);
            _audio.PlayPredicted(scannerUser.PingSound, uid, uid);
        }
    }
}
