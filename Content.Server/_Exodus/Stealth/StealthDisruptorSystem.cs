using System.Numerics;
using Content.Server._Exodus.Cartridges;
using Content.Shared._Exodus.Stealth.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;

namespace Content.Server._Exodus.Stealth;

public sealed partial class StealthDisruptorSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedStealthSystem _stealth = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private ExamineSystemShared _examine = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StealthDisruptorComponent, UseInHandEvent>(OnUse);
    }

    private void OnUse(Entity<StealthDisruptorComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || ent.Comp.Range <= 0 || ent.Comp.SuppressionDuration <= TimeSpan.Zero ||
            !_useDelay.TryGetDelayInfo(ent.Owner, out _) || _useDelay.IsDelayed(ent.Owner))
            return;

        if (!TryConsumeCartridge(ent))
        {
            _popup.PopupEntity(Loc.GetString("stealth-disruptor-no-cartridge"), ent, args.User);
            return;
        }

        _useDelay.TryResetDelay(ent.Owner);
        args.Handled = true;
        args.ApplyDelay = false;
        _audio.PlayPvs(ent.Comp.ActivationSound, ent);
        var center = _transform.GetMapCoordinates(args.User);
        // A typed stealth lookup skips ordinary lockers, and therefore their cloaked occupants.
        // The untyped query includes nested containers before filtering for supported concealment.
        var targets = _lookup.GetEntitiesInRange(center, ent.Comp.Range, LookupFlags.All);
        var signaled = new HashSet<EntityUid>();
        foreach (var uid in targets)
        {
            if (TerminatingOrDeleted(uid) || _whitelist.IsWhitelistFail(ent.Comp.TargetWhitelist, uid))
                continue;

            var marker = _containers.TryGetOuterContainer(uid, Transform(uid), out var container) ? container.Owner : uid;
            var position = _transform.GetMapCoordinates(marker);
            if (position.MapId != center.MapId ||
                Vector2.DistanceSquared(position.Position, center.Position) > ent.Comp.Range * ent.Comp.Range ||
                ent.Comp.RequiresLineOfSight && !_examine.InRangeUnOccluded(marker, center, ent.Comp.Range) ||
                !_stealth.TrySuppress(uid, ent.Comp.SuppressionDuration))
                continue;

            // Several cloaked occupants of one locker produce a single visible marker and sound.
            if (!signaled.Add(marker))
                continue;

            SpawnAttachedTo(ent.Comp.RevealEffect, new EntityCoordinates(marker, Vector2.Zero));
            _audio.PlayPvs(ent.Comp.RevealSound, marker);
        }

        _popup.PopupEntity(Loc.GetString("stealth-disruptor-pulse"), ent, args.User);
    }

    private bool TryConsumeCartridge(Entity<StealthDisruptorComponent> ent)
    {
        if (!_itemSlots.TryGetSlot(ent.Owner, ent.Comp.CartridgeSlot, out var slot) ||
            slot.ContainerSlot is not { } container || slot.Item is not { } cartridge ||
            TerminatingOrDeleted(cartridge) || EntityManager.IsQueuedForDeletion(cartridge) ||
            !TryComp<DisposableCartridgeComponent>(cartridge, out var disposable) ||
            !_containers.Remove(cartridge, container))
            return false;

        QueueDel(cartridge);
        SpawnInContainerOrDrop(disposable.SpentPrototype, ent.Owner, ent.Comp.CartridgeSlot);
        return true;
    }
}
