using Content.Server.Administration.Logs;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Prying.Components;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private IAdminLogManager _admin = default!;
    [Dependency] private SharedDoorSystem _doors = default!;

    private EntityQuery<HandsComponent> _handsQuery;

    private void InitializePrying()
    {
        _handsQuery = GetEntityQuery<HandsComponent>();
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticPryEvent>(OnPry);
        SubscribeLocalEvent<GeneticEffectsComponent, BeforeInteractHandEvent>(OnPryInteractHand);
        SubscribeLocalEvent<GeneticEffectsComponent, DoAfterAttemptEvent<GeneticPryDoAfterEvent>>(OnPryAttempt);
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticPryDoAfterEvent>(OnPried);
    }

    private bool CanPry(EntityUid user, EntityUid target)
    {
        if (!CanUse(user, GeneticAbility.ForcePry) || TerminatingOrDeleted(target) ||
            !_hands.TryGetActiveHand(user, out var hand) || hand.HeldEntity != null ||
            !_interaction.IsAccessible(user, target) || !_interaction.InRangeUnobstructed(user, target) ||
            !TryComp<DoorComponent>(target, out var door) ||
            door.State is not (DoorState.Closed or DoorState.Denying))
            return false;
        var attempt = new BeforePryEvent(user, true, true, true);
        RaiseLocalEvent(target, ref attempt);
        return !attempt.Cancelled;
    }

    private void OnPry(Entity<GeneticEffectsComponent> ent, ref GeneticPryEvent args)
    {
        if (!args.Handled)
            args.Handled = TryStartPry(ent, args.Target);
    }

    private void OnPryInteractHand(Entity<GeneticEffectsComponent> ent, ref BeforeInteractHandEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.ForcePry) ||
            !TryComp<DoorComponent>(args.Target, out var door) || !door.ClickOpen ||
            door.State is not (DoorState.Closed or DoorState.Denying) ||
            _doors.CanOpen(args.Target, door, ent.Owner))
            return;

        // Normal door interaction takes priority; force only doors that refuse to open.
        args.Handled = TryStartPry(ent, args.Target);
    }

    private bool TryStartPry(Entity<GeneticEffectsComponent> ent, EntityUid target)
    {
        if (!CanPry(ent, target))
            return false;

        Reveal(ent);
        var state = EnsureComp<GeneticAbilityStateComponent>(ent);
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, ent, state.PryTime,
            new GeneticPryDoAfterEvent(), ent, target)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
            DistanceThreshold = 1.5f,
            AttemptFrequency = AttemptFrequency.EveryTick,
        });
    }

    private void OnPryAttempt(Entity<GeneticEffectsComponent> ent, ref DoAfterAttemptEvent<GeneticPryDoAfterEvent> args)
    {
        if (ent.Comp.Reverting || (ent.Comp.Modifiers.Abilities & GeneticAbility.ForcePry) == 0 ||
            !_handsQuery.TryComp(ent, out var hands) || hands.ActiveHand is not { HeldEntity: null })
            args.Cancel();
    }

    private void OnPried(Entity<GeneticEffectsComponent> ent, ref GeneticPryDoAfterEvent args)
    {
        if (args.Handled || args.Cancelled || args.Target is not { } target || !CanPry(ent, target))
            return;
        args.Handled = true;
        Reveal(ent);
        if (TryComp<GeneticAbilityStateComponent>(ent, out var state))
            _audio.PlayPvs(state.PrySound, target);
        // The access-denied animation must not block an instant pry.
        if (TryComp<DoorComponent>(target, out var door) && door.State == DoorState.Denying)
            _doors.SetState(target, DoorState.Closed, door);
        var ev = new PriedEvent(ent);
        RaiseLocalEvent(target, ref ev);
        _admin.Add(LogType.Action, LogImpact.High, $"{ToPrettyString(ent):user} genetically pried {ToPrettyString(target):target}");
    }
}
