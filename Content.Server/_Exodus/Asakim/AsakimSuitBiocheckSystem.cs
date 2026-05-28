using Content.Server.Administration.Logs;
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Exodus.Asakim;
using Content.Shared.Clothing;
using Content.Shared.Database;
using Content.Shared.Inventory;
using Content.Shared.Mind.Components;
using Content.Shared.Mobs.Systems;

namespace Content.Server._Exodus.Asakim;

/// <summary>
/// Fires the biocheck trigger on Asakim-biocoded clothing when a non-Asakim wearer is detected.
/// The actual reaction (gib, explosion, etc.) is defined by trigger behaviors on the clothing prototype.
/// Two trigger paths:
/// 1. Equipped while alive and not Asakim.
/// 2. A mind becomes attached to the wearer's body and that body does not currently have an
///    Asakim brain organ (e.g. someone transplanted a non-Asakim brain into an Asakim body
///    and the new owner woke up).
/// </summary>
public sealed class AsakimSuitBiocheckSystem : EntitySystem
{
    [Dependency] private readonly AsakimIdentitySystem _asakim = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly TriggerSystem _trigger = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AsakimSuitBiocheckComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<MindContainerComponent, MindAddedMessage>(OnMindAdded);
    }

    private void OnEquipped(Entity<AsakimSuitBiocheckComponent> ent, ref ClothingGotEquippedEvent args)
    {
        var wearer = args.Wearer;
        if (!_mobState.IsAlive(wearer))
            return;

        if (_asakim.IsAsakim(wearer))
            return;

        RejectWearer(ent, wearer, "equipped while not Asakim");
    }

    private void OnMindAdded(Entity<MindContainerComponent> body, ref MindAddedMessage args)
    {
        if (_asakim.IsAsakim(body))
            return;

        if (!_inventory.TryGetSlotEntity(body, "outerClothing", out var outerUid))
            return;

        if (!TryComp<AsakimSuitBiocheckComponent>(outerUid, out var biocheck))
            return;

        RejectWearer((outerUid.Value, biocheck), body, "mind attached without Asakim brain");
    }

    private void RejectWearer(Entity<AsakimSuitBiocheckComponent> ent, EntityUid wearer, string reason)
    {
        _adminLogger.Add(LogType.Trigger, LogImpact.High,
            $"Asakim suit biocheck on {ToPrettyString(ent.Owner):suit} fired its trigger against {ToPrettyString(wearer):wearer} ({reason})");
        _trigger.Trigger(ent.Owner, wearer);
    }
}
