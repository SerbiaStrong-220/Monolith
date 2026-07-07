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

public sealed class LifeInsuranceScannerSystem : EntitySystem
{
    [Dependency] private ContainerSystem _container = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private ClimbSystem _climb = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;

    public const string ContainerId = "life-insurance-scanner-body";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<LifeInsuranceScannerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, ContainerRelayMovementEntityEvent>(OnRelayMovement);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, GetVerbsEvent<AlternativeVerb>>(AddAlternativeVerbs);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, DragDropTargetEvent>(OnDragDropOn, before: new[] { typeof(ClimbSystem) });
        SubscribeLocalEvent<LifeInsuranceScannerComponent, CanDropTargetEvent>(OnCanDragDropOn);
        SubscribeLocalEvent<LifeInsuranceScannerComponent, LifeInsuranceScannerEnterDoAfterEvent>(OnEnterDoAfter);
    }

    private void OnInit(Entity<LifeInsuranceScannerComponent> ent, ref ComponentInit args)
    {
        ent.Comp.BodyContainer = _container.EnsureContainer<ContainerSlot>(ent, ContainerId);
    }

    public bool IsOccupied(LifeInsuranceScannerComponent comp)
    {
        return comp.BodyContainer.ContainedEntity != null;
    }

    public bool CanInsert(EntityUid target)
    {
        return HasComp<BodyComponent>(target);
    }

    private void OnCanDragDropOn(Entity<LifeInsuranceScannerComponent> ent, ref CanDropTargetEvent args)
    {
        args.Handled = true;
        args.CanDrop |= CanInsert(args.Dragged);
    }

    private void OnRelayMovement(Entity<LifeInsuranceScannerComponent> ent, ref ContainerRelayMovementEntityEvent args)
    {
        if (!_blocker.CanInteract(args.Entity, ent))
            return;

        EjectBody(ent, ent.Comp);
    }

    private void AddAlternativeVerbs(Entity<LifeInsuranceScannerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        if (IsOccupied(ent.Comp))
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => EjectBody(ent, ent.Comp),
                Category = VerbCategory.Eject,
                Text = Loc.GetString("medical-scanner-verb-noun-occupant"),
                Priority = 1
            });
        }

        if (!IsOccupied(ent.Comp) && CanInsert(args.User) && _blocker.CanMove(args.User))
        {
            var user = args.User;
            args.Verbs.Add(new AlternativeVerb
            {
                Act = () => TryEnter(ent, user, user, ent.Comp),
                Text = Loc.GetString("medical-scanner-verb-enter")
            });
        }
    }

    private void OnDestroyed(Entity<LifeInsuranceScannerComponent> ent, ref DestructionEventArgs args)
    {
        EjectBody(ent, ent.Comp);
    }

    private void OnDragDropOn(Entity<LifeInsuranceScannerComponent> ent, ref DragDropTargetEvent args)
    {
        TryEnter(ent, args.User, args.Dragged, ent.Comp);
        args.Handled = true;
    }

    private void OnEnterDoAfter(Entity<LifeInsuranceScannerComponent> ent, ref LifeInsuranceScannerEnterDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Args.Target is not { } target)
            return;

        InsertBody(ent, target, ent.Comp);
        args.Handled = true;
    }

    /// <summary>
    /// Timed climb-into-capsule. The body is only inserted once the progress bar completes.
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
