using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Popups;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Shared._Exodus.Biocode;

/// <summary>
/// Enforces <see cref="BodySlotInsertGuardComponent"/>: cancels insertion of organs/parts that do
/// not match the whitelist into the guarded slots. Catches every insertion (surgery included) via
/// the container attempt event.
/// </summary>
public sealed class BodySlotInsertGuardSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BodySlotInsertGuardComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BodySlotInsertGuardComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
    }

    private void OnStartup(Entity<BodySlotInsertGuardComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.SpawnTick = _timing.CurTick;
    }

    private void OnInsertAttempt(Entity<BodySlotInsertGuardComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Cancelled)
            return;

        // The body is assembled (organs/parts inserted) on the same tick the guarded part spawns.
        // Skip insertions on that tick (initial assembly); only guard later ones (surgery).
        if (_timing.CurTick == ent.Comp.SpawnTick)
            return;

        var containerId = args.Container.ID;
        var inserted = args.EntityUid;

        // Organ slot: the inserted organ itself must pass the whitelist.
        foreach (var slot in ent.Comp.OrganSlots)
        {
            if (containerId != SharedBodySystem.GetOrganContainerId(slot))
                continue;

            if (IsAllowed(ent.Comp.Whitelist, inserted))
                return;

            Reject(ent, args);
            return;
        }

        // Part slot: the inserted part must carry an organ that passes the whitelist.
        foreach (var slot in ent.Comp.PartSlots)
        {
            if (containerId != SharedBodySystem.GetPartSlotContainerId(slot))
                continue;

            if (PartHasMatchingOrgan(inserted, ent.Comp.Whitelist))
                return;

            Reject(ent, args);
            return;
        }
    }

    private bool IsAllowed(EntityWhitelist? whitelist, EntityUid inserted)
    {
        return whitelist != null && _whitelist.IsValid(whitelist, inserted);
    }

    private bool PartHasMatchingOrgan(EntityUid part, EntityWhitelist? whitelist)
    {
        if (whitelist == null || !TryComp<BodyPartComponent>(part, out var partComp))
            return false;

        foreach (var (organId, _) in _body.GetPartOrgans(part, partComp))
        {
            if (_whitelist.IsValid(whitelist, organId))
                return true;
        }

        return false;
    }

    private void Reject(Entity<BodySlotInsertGuardComponent> ent, ContainerIsInsertingAttemptEvent args)
    {
        args.Cancel();
        _popup.PopupEntity(Loc.GetString(ent.Comp.RejectPopup), ent.Owner);
    }
}
