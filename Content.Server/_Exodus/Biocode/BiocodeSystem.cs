using Content.Server.Administration.Logs;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Exodus.Biocode;
using Content.Shared.Clothing;
using Content.Shared.Database;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._Exodus.Biocode;

/// <summary>
/// Server side of the biocode gate: fires the entity's trigger when a non-authorized live wearer
/// is detected, either by equipping the item while alive or by a mind attaching to the wearer's
/// body. The reaction itself (gib, explosion, etc.) is defined by reject handlers or trigger
/// behaviors on the prototype.
/// </summary>
public sealed class BiocodeSystem : SharedBiocodeSystem
{
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BiocodeComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<MindContainerComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnEquipped(Entity<BiocodeComponent> ent, ref ClothingGotEquippedEvent args)
    {
        if (!ent.Comp.TriggerOnReject)
            return;

        var wearer = args.Wearer;
        if (!_mobState.IsAlive(wearer) || IsAllowed(ent, wearer))
            return;

        RejectTrigger(ent, wearer, "equipped while not authorized");
    }

    private void OnMindAdded(Entity<MindContainerComponent> body, ref MindAddedMessage args)
    {
        if (!_mobState.IsAlive(body.Owner))
            return;

        var enumerator = _inventory.GetSlotEnumerator(body.Owner);
        while (enumerator.NextItem(out var item))
        {
            if (!TryComp<BiocodeComponent>(item, out var biocode) || !biocode.TriggerOnReject)
                continue;

            if (IsAllowed((item, biocode), body.Owner))
                continue;

            RejectTrigger((item, biocode), body.Owner, "mind attached while not authorized");
        }
    }

    /// <summary>
    /// <paramref name="adminLogReason"/> is a plain English tag for the admin log and the
    /// <see cref="BiocodeRejectedEvent"/> payload — it is never shown to the player and is
    /// intentionally not localized.
    /// </summary>
    private void RejectTrigger(Entity<BiocodeComponent> ent, EntityUid wearer, string adminLogReason)
    {
        _adminLogger.Add(LogType.Trigger, LogImpact.High,
            $"Biocode on {ToPrettyString(ent.Owner):item} fired its trigger against {ToPrettyString(wearer):wearer} ({adminLogReason})");

        var ev = new BiocodeRejectedEvent(ent.Owner, wearer, adminLogReason);
        RaiseLocalEvent(ent.Owner, ref ev);
        if (ev.Handled)
            return;

        _trigger.Trigger(ent.Owner, wearer);
    }
}

[ByRefEvent]
public record struct BiocodeRejectedEvent(EntityUid Item, EntityUid User, string Reason)
{
    public bool Handled;
}
