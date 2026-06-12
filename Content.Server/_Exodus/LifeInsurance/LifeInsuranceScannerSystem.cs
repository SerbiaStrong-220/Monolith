using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Destructible;
using Content.Shared.DragDrop;
using Content.Shared.Movement.Events;
using Content.Shared.Verbs;
using Robust.Server.Containers;
using Robust.Shared.Containers;

namespace Content.Server._Exodus.LifeInsurance;

/// <summary>
/// Patient scanning capsule of the life insurance machine. Handles inserting/ejecting a body;
/// the actual DNA recording is driven from the linked console.
/// </summary>
public sealed class LifeInsuranceScannerSystem : EntitySystem
{
    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly ClimbSystem _climb = default!;
    [Dependency] private readonly LifeInsuranceConsoleSystem _console = default!; // TEMP: auto-record on insert

    public const string ContainerId = "life-insurance-scanner-body";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LifeInsuranceScannerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, ContainerRelayMovementEntityEvent>(OnRelayMovement);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, GetVerbsEvent<InteractionVerb>>(AddInsertOtherVerb);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, GetVerbsEvent<AlternativeVerb>>(AddAlternativeVerbs);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, DragDropTargetEvent>(OnDragDropOn);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, CanDropTargetEvent>(OnCanDragDropOn);
    }

    private void OnInit(EntityUid uid, LifeInsuranceScannerComponent comp, ComponentInit args)
    {
        comp.BodyContainer = _container.EnsureContainer<ContainerSlot>(uid, ContainerId);
    }

    public bool IsOccupied(LifeInsuranceScannerComponent comp)
    {
        return comp.BodyContainer.ContainedEntity != null;
    }

    public bool CanInsert(EntityUid target)
    {
        return HasComp<BodyComponent>(target);
    }

    private void OnCanDragDropOn(EntityUid uid, LifeInsuranceScannerComponent comp, ref CanDropTargetEvent args)
    {
        args.Handled = true;
        args.CanDrop |= CanInsert(args.Dragged);
    }

    private void OnRelayMovement(EntityUid uid, LifeInsuranceScannerComponent comp, ref ContainerRelayMovementEntityEvent args)
    {
        if (!_blocker.CanInteract(args.Entity, uid))
            return;

        EjectBody(uid, comp);
    }

    private void AddInsertOtherVerb(EntityUid uid, LifeInsuranceScannerComponent comp, GetVerbsEvent<InteractionVerb> args)
    {
        if (args.Using == null ||
            !args.CanAccess ||
            !args.CanInteract ||
            IsOccupied(comp) ||
            !CanInsert(args.Using.Value))
            return;

        var name = "Unknown";
        if (TryComp(args.Using.Value, out MetaDataComponent? metadata))
            name = metadata.EntityName;

        var target = args.Target;
        args.Verbs.Add(new InteractionVerb
        {
            Act = () => InsertBody(uid, target, comp),
            Category = VerbCategory.Insert,
            Text = name
        });
    }

    private void AddAlternativeVerbs(EntityUid uid, LifeInsuranceScannerComponent comp, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (IsOccupied(comp))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => EjectBody(uid, comp),
                Category = VerbCategory.Eject,
                Text = Loc.GetString("medical-scanner-verb-noun-occupant"),
                Priority = 1
            });
        }

        if (!IsOccupied(comp) && CanInsert(args.User) && _blocker.CanMove(args.User))
        {
            var user = args.User;
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => InsertBody(uid, user, comp),
                Text = Loc.GetString("medical-scanner-verb-enter")
            });
        }
    }

    private void OnDestroyed(EntityUid uid, LifeInsuranceScannerComponent comp, DestructionEventArgs args)
    {
        EjectBody(uid, comp);
    }

    private void OnDragDropOn(EntityUid uid, LifeInsuranceScannerComponent comp, ref DragDropTargetEvent args)
    {
        InsertBody(uid, args.Dragged, comp);
    }

    public void InsertBody(EntityUid uid, EntityUid toInsert, LifeInsuranceScannerComponent comp)
    {
        if (comp.BodyContainer.ContainedEntity != null)
            return;

        if (!CanInsert(toInsert))
            return;

        _container.Insert(toInsert, comp.BodyContainer);

        // TEMP (single-player testing): auto-record DNA the moment a body is inserted.
        _console.TryAutoRecordFromScanner(uid, toInsert);
    }

    public void EjectBody(EntityUid uid, LifeInsuranceScannerComponent comp)
    {
        if (comp.BodyContainer.ContainedEntity is not { Valid: true } contained)
            return;

        _container.Remove(contained, comp.BodyContainer);
        _climb.ForciblySetClimbing(contained, uid);
    }
}
