using Content.Shared._Exodus.LifeInsurance.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Components;
using Content.Shared.Climbing.Systems;
using Content.Shared.Destructible;
using Content.Shared.DoAfter;
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
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private ClimbSystem _climb = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private LifeInsuranceConsoleSystem _console = default!; // TEMP: auto-record on insert

    public const string ContainerId = "life-insurance-scanner-body";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LifeInsuranceScannerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, ContainerRelayMovementEntityEvent>(OnRelayMovement);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, GetVerbsEvent<AlternativeVerb>>(AddAlternativeVerbs);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, DragDropTargetEvent>(OnDragDropOn);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, CanDropTargetEvent>(OnCanDragDropOn);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, LifeInsuranceScannerEnterDoAfterEvent>(OnEnterDoAfter);
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
                Act = () => TryEnter(uid, user, user, comp),
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
        TryEnter(uid, args.User, args.Dragged, comp);
        args.Handled = true;
    }

    private void OnEnterDoAfter(EntityUid uid, LifeInsuranceScannerComponent comp, LifeInsuranceScannerEnterDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Target is not { } target)
            return;

        InsertBody(uid, target, comp);
        args.Handled = true;
    }

    /// <summary>
    /// Starts the timed climb-into-capsule. The body is only inserted once the progress bar completes,
    /// so the entry takes exactly as long as the bar shows.
    /// </summary>
    private void TryEnter(EntityUid uid, EntityUid user, EntityUid target, LifeInsuranceScannerComponent comp)
    {
        if (IsOccupied(comp) || !CanInsert(target))
            return;

        var args = new DoAfterArgs(EntityManager, user, comp.EnterDelay, new LifeInsuranceScannerEnterDoAfterEvent(), uid, target: target, used: uid)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false,
        };
        _doAfter.TryStartDoAfter(args);
    }

    public void InsertBody(EntityUid uid, EntityUid toInsert, LifeInsuranceScannerComponent comp)
    {
        if (comp.BodyContainer.ContainedEntity != null)
            return;

        if (!CanInsert(toInsert))
            return;

        if (!_container.Insert(toInsert, comp.BodyContainer))
            return;

        _appearance.SetData(uid, LifeInsuranceScannerVisuals.State, LifeInsuranceScannerState.Occupied);

        // TEMP (single-player testing): auto-record DNA the moment a body is inserted.
        _console.TryAutoRecordFromScanner(uid, toInsert);
    }

    public void EjectBody(EntityUid uid, LifeInsuranceScannerComponent comp)
    {
        if (comp.BodyContainer.ContainedEntity is not { Valid: true } contained)
            return;

        _container.Remove(contained, comp.BodyContainer);
        _appearance.SetData(uid, LifeInsuranceScannerVisuals.State, LifeInsuranceScannerState.Open);
        _climb.ForciblySetClimbing(contained, uid);
    }
}
