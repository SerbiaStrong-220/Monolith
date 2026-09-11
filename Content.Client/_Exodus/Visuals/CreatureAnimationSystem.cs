using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Weapons.Melee;
using Robust.Client.GameObjects;
using Robust.Shared.Physics.Components;
using Robust.Shared.Timing;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._Exodus.Visuals;

/// <summary>Coordinates one-shot and looping animations without competing sprite writers.</summary>
public sealed class CreatureAnimationSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<MobStateComponent> _mobs;
    private EntityQuery<MeleeWeaponComponent> _melee;
    private EntityQuery<PhysicsComponent> _physics;

    public override void Initialize()
    {
        base.Initialize();
        _mobs = GetEntityQuery<MobStateComponent>();
        _melee = GetEntityQuery<MeleeWeaponComponent>();
        _physics = GetEntityQuery<PhysicsComponent>();
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<CreatureAnimationVisualsComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var animation, out var sprite))
        {
            var dead = _mobs.TryGetComponent(uid, out var mob) && mob.CurrentState == MobState.Dead;
            if (!animation.AnimationStarted)
            {
                animation.AnimationStarted = true;
                animation.WasDead = dead;
                animation.SpawnUntil = now + animation.SpawnDuration;
            }

            if (dead && !animation.WasDead)
                animation.DeathUntil = now + animation.DeathDuration;
            animation.WasDead = dead;

            if (dead && sprite.DrawDepth > (int)DrawDepth.DeadMobs)
            {
                animation.OriginalDrawDepth ??= sprite.DrawDepth;
                _sprite.SetDrawDepth((uid, sprite), (int)DrawDepth.DeadMobs);
            }
            else if (!dead && animation.OriginalDrawDepth is { } originalDepth)
            {
                _sprite.SetDrawDepth((uid, sprite), originalDepth);
                animation.OriginalDrawDepth = null;
            }

            if (_melee.TryGetComponent(uid, out var melee) && melee.NextAttack != animation.LastAttack)
            {
                animation.LastAttack = melee.NextAttack;
                if (melee.NextAttack > now)
                    animation.AttackUntil = now + animation.AttackDuration;
            }

            var state = animation.IdleState;
            if (dead)
                state = now < animation.DeathUntil
                    ? animation.DyingState ?? animation.DeadState ?? state
                    : animation.DeadState ?? state;
            else if (now < animation.AttackUntil && animation.AttackState is { } attack)
                state = attack;
            else if (now < animation.SpawnUntil && animation.SpawnState is { } spawn)
                state = spawn;
            else if (animation.MovingState is { } moving && _physics.TryGetComponent(uid, out var physics)
                && physics.LinearVelocity.LengthSquared() > 0.01f)
                state = moving;

            if (state == animation.CurrentState)
                continue;

            animation.CurrentState = state;
            _sprite.LayerSetRsiState((uid, sprite), animation.Layer, state);
            _sprite.LayerSetAnimationTime((uid, sprite), animation.Layer, 0);
        }
    }
}
