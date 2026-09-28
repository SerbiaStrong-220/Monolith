using Content.Shared._Exodus.Stances;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Components;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Stances;

public sealed partial class LocomotionStanceVisualsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<SpriteMovementComponent> _movementQuery;

    public override void Initialize()
    {
        base.Initialize();
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _movementQuery = GetEntityQuery<SpriteMovementComponent>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var query = EntityQueryEnumerator<LocomotionStanceVisualsComponent, LocomotionStanceComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var visuals, out var stance, out var sprite))
        {
            var state = GetState((uid, stance));
            if (visuals.CurrentState == state || !_sprite.LayerMapTryGet((uid, sprite), visuals.Layer, out var layer, false))
                continue;

            visuals.CurrentState = state;
            _sprite.LayerSetRsiState((uid, sprite), layer, state);
        }
    }

    private string GetState(Entity<LocomotionStanceComponent> ent)
    {
        var (uid, stance) = ent;
        if (_mobQuery.TryComp(uid, out var mob))
        {
            if (mob.CurrentState == MobState.Dead)
                return "dead";
            if (mob.CurrentState == MobState.Critical)
                return stance.Stance == LocomotionStance.Curled ? "curled" : "critical";
        }

        if (stance.TransitionEnd != TimeSpan.Zero)
        {
            if (stance.Stance == LocomotionStance.Curled)
                return stance.PreviousStance == LocomotionStance.Upright ? "curl-upright" : "curl";
            if (stance.PreviousStance == LocomotionStance.Curled)
                return stance.Stance == LocomotionStance.Upright ? "uncurl-upright" : "uncurl";
            return stance.Stance == LocomotionStance.Upright ? "rise" : "lower";
        }

        if (stance.Stance == LocomotionStance.Curled)
            return "curled";
        if (_timing.CurTime < stance.AttackAnimationEnd)
            return stance.Stance == LocomotionStance.Upright ? "claw" : "bite";

        var moving = _movementQuery.TryComp(uid, out var movement) && movement.IsMoving;
        return stance.Stance == LocomotionStance.Upright
            ? moving ? "walk" : "upright"
            : moving ? "run" : "quadruped";
    }
}
