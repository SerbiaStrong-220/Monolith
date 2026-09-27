using Content.Shared._Exodus.DoAfter;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Sound.Components;
using Content.Shared.Whitelist;
using Robust.Shared.Player;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    private void InitializeTelekinesis()
    {
        SubscribeLocalEvent<GeneticEffectsComponent, GeneticTelekinesisEvent>(OnTelekinesis);
        SubscribeLocalEvent<GeneticAbilityStateComponent, InRangeOverrideEvent>(OnTelekinesisRange);
        SubscribeLocalEvent<GeneticAbilityStateComponent, BeforeDoAfterStartEvent>(OnTelekinesisDoAfter);
        SubscribeLocalEvent<GeneticAbilityStateComponent, BuckleAttemptEvent>(OnTelekinesisBuckle);
    }

    private bool InTelekinesisRange(Entity<GeneticAbilityStateComponent> ent, EntityUid target)
    {
        if (TerminatingOrDeleted(target) || !_interaction.IsAccessible(ent.Owner, target))
            return false;

        var transform = Transform(target);
        // This overload does not raise InRangeOverrideEvent again.
        return _interaction.InRangeUnobstructed(ent.Owner, target, transform.Coordinates,
            transform.LocalRotation, range: ent.Comp.TelekinesisRange);
    }

    private void OnTelekinesis(Entity<GeneticEffectsComponent> ent, ref GeneticTelekinesisEvent args)
    {
        if (args.Handled || !CanUse(ent, GeneticAbility.Telekinesis) || TerminatingOrDeleted(args.Target) ||
            args.Target == ent.Owner || HasComp<MobStateComponent>(args.Target) ||
            !_blocker.CanInteract(ent, args.Target))
            return;

        var state = EnsureComp<GeneticAbilityStateComponent>(ent);
        if (!InTelekinesisRange((ent.Owner, state), args.Target))
        {
            _popup.PopupEntity(Loc.GetString("genetics-telekinesis-unreachable"), ent, ent);
            return;
        }

        _hands.TryGetActiveItem(ent.Owner, out var held);
        var targetTransform = Transform(args.Target);
        var initialPosition = targetTransform.Coordinates;
        var initialRotation = targetTransform.LocalRotation;
        var userPosition = Transform(ent).Coordinates;
        var previousTarget = state.TelekinesisTarget;
        var previousTool = state.TelekinesisTool;
        state.TelekinesisTarget = args.Target;
        state.TelekinesisTool = held;
        try
        {
            Reveal(ent);
            // Use the ordinary interaction chain so target-specific handlers and permissions still apply.
            args.Handled = held is { } tool
                ? _interaction.InteractUsing(ent, tool, args.Target, Transform(args.Target).Coordinates)
                : _interaction.InteractHand(ent, args.Target);
        }
        finally
        {
            state.TelekinesisTarget = previousTarget;
            state.TelekinesisTool = previousTool;
        }

        if (args.Handled)
            PlayTelekinesisFeedback((ent.Owner, state), args.Target, held);

        if (args.Handled && held is { } placed && !TerminatingOrDeleted(placed) &&
            !_hands.IsHolding(ent.Owner, placed) && !_containers.IsEntityInContainer(placed) &&
            userPosition.IsValid(EntityManager) && MetaData(placed).VisibilityMask == MetaData(ent).VisibilityMask)
        {
            var animation = new GeneticTelekinesisAnimationEvent(GetNetEntity(placed), GetNetCoordinates(userPosition),
                GetNetCoordinates(Transform(placed).Coordinates), state.TelekinesisPlacementDuration);
            RaiseNetworkEvent(animation, Filter.Pvs(placed).AddPlayersByPvs(ent.Owner));
        }

        // Normal pickup animates locally on the predicting client and excludes that player from the
        // server event. Telekinesis only runs on the server, so send the missing animation to its user.
        if (held == null && args.Handled && !TerminatingOrDeleted(ent) && !TerminatingOrDeleted(args.Target) &&
            initialPosition.IsValid(EntityManager) && _hands.IsHolding(ent.Owner, args.Target) &&
            MetaData(args.Target).VisibilityMask == MetaData(ent).VisibilityMask &&
            TryComp<ActorComponent>(ent, out var actor))
        {
            var animation = new PickupAnimationEvent(GetNetEntity(args.Target), GetNetCoordinates(initialPosition),
                GetNetCoordinates(Transform(ent).Coordinates), initialRotation);
            RaiseNetworkEvent(animation, actor.PlayerSession);
        }

        if (!args.Handled)
        {
            var message = held != null ? "genetics-telekinesis-held-failed" : "genetics-telekinesis-failed";
            _popup.PopupEntity(Loc.GetString(message), ent, ent);
        }
    }

    private void PlayTelekinesisFeedback(Entity<GeneticAbilityStateComponent> ent, EntityUid target, EntityUid? held)
    {
        var user = ent.Owner;
        if (TerminatingOrDeleted(user))
            return;
        var source = TerminatingOrDeleted(target) ? user : target;
        _audio.PlayPvs(ent.Comp.TelekinesisSound, source);

        // Ordinary predicted sounds exclude the actor. This interaction has no client-side prediction.
        if (!TryComp<ActorComponent>(user, out var actor))
            return;
        if (held == null && !TerminatingOrDeleted(target) && _hands.IsHolding(user, target) &&
            TryComp<EmitSoundOnPickupComponent>(target, out var pickup))
            _audio.PlayEntity(pickup.Sound, actor.PlayerSession, user);
        if (held is not { } item || TerminatingOrDeleted(item))
            return;
        if (!_hands.IsHolding(user, item) && TryComp<EmitSoundOnDropComponent>(item, out var drop))
            _audio.PlayEntity(drop.Sound, actor.PlayerSession, item);
        if (!TerminatingOrDeleted(target) && TryComp<EmitSoundOnInteractUsingComponent>(target, out var usingSound) &&
            _whitelist.IsWhitelistPass(usingSound.Whitelist, item))
            _audio.PlayEntity(usingSound.Sound, actor.PlayerSession, target);
    }

    private void OnTelekinesisRange(Entity<GeneticAbilityStateComponent> ent, ref InRangeOverrideEvent args)
    {
        if (args.Handled || args.Target != ent.Comp.TelekinesisTarget || !CanUse(ent, GeneticAbility.Telekinesis))
            return;

        args.Handled = true;
        args.InRange = InTelekinesisRange(ent, args.Target);
    }

    private void OnTelekinesisDoAfter(Entity<GeneticAbilityStateComponent> ent, ref BeforeDoAfterStartEvent args)
    {
        var interaction = args.Args;
        var tool = ent.Comp.TelekinesisTool ?? ent.Owner;
        if (ent.Comp.TelekinesisTarget is not { } target || interaction.Target != target ||
            interaction.Used != null && interaction.Used != tool || interaction.RangeProvider != null ||
            interaction.DistanceThreshold != null || !CanUse(ent, GeneticAbility.Telekinesis))
            return;

        interaction.DistanceThreshold = ent.Comp.TelekinesisRange;
        interaction.RangeProvider = SharedGeneticEffectsSystem.TelekinesisRangeProvider;
        interaction.PredictSound = false;
    }

    private void OnTelekinesisBuckle(Entity<GeneticAbilityStateComponent> ent, ref BuckleAttemptEvent args)
    {
        // InteractHand on a chair must not move the user's body across the remote interaction range.
        args.Cancelled |= ent.Comp.TelekinesisTarget != null;
    }
}
