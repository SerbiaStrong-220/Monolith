// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Emoting;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Gravity;
using Content.Shared.Movement.Components;
using Content.Shared.Throwing;

namespace Content.Shared._Goobstation.Dash;

public sealed partial class DashActionSystem : EntitySystem // Exodus: generated dependency injection.
{
    [Dependency] private SharedActionsSystem _actions = default!; // Exodus: generated dependency injection.
    [Dependency] private SharedGravitySystem _gravity = default!; // Exodus: generated dependency injection.
    [Dependency] private ThrowingSystem _throwing = default!; // Exodus: generated dependency injection.
    [Dependency] private SharedTransformSystem _transform = default!; // Exodus: generated dependency injection.
    [Dependency] private StaminaSystem _stamina = default!; // Exodus: generated dependency injection.

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DashActionEvent>(OnDashAction);

        SubscribeLocalEvent<DashActionComponent, ComponentInit>(OnComponentInit);
        SubscribeLocalEvent<DashActionComponent, ComponentShutdown>(OnComponentShutdown);
    }

    private void OnDashAction(DashActionEvent args)
    {
        if (args.Handled)
            return;

        if (args.NeedsGravity && _gravity.IsWeightless(args.Performer))
            return;

        args.Handled = true;
        var vec = (_transform.ToMapCoordinates(args.Target).Position -
                   _transform.GetMapCoordinates(args.Performer).Position).Normalized() * args.Distance;

        var speed = args.Speed;

        if (args.AffectedBySpeed && TryComp<MovementSpeedModifierComponent>(args.Performer, out var speedcomp))
        {
            vec *= speedcomp.CurrentSprintSpeed / speedcomp.BaseSprintSpeed;
            speed *= speedcomp.CurrentSprintSpeed / speedcomp.BaseSprintSpeed;
        }

        _throwing.TryThrow( args.Performer, vec, baseThrowSpeed: speed, user: null, pushbackRatio: 0, friction: null, compensateFriction: false, recoil: false, animated: false);

        if (args.StaminaDrain != null)
            _stamina.TakeStaminaDamage(args.Performer, args.StaminaDrain.Value, visual: false, immediate: false);

        if (args.Emote != null && TryComp<AnimatedEmotesComponent>(args.Performer, out var emotes))
        {
            emotes.Emote = args.Emote;
            Dirty(args.Performer, emotes);
        }
    }

    private void OnComponentInit(EntityUid uid, DashActionComponent comp, ref ComponentInit args)
    {
        comp.ActionUid = _actions.AddAction(uid, comp.ActionProto);
    }

    private void OnComponentShutdown(EntityUid uid, DashActionComponent comp, ref ComponentShutdown args)
    {
        _actions.RemoveAction(comp.ActionUid);
    }
}
