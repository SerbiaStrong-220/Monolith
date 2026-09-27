using System.Numerics;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Projectiles;
using Content.Shared.Sprite;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Reflect;
using Robust.Shared.Map;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Exodus.Genetics;

public sealed partial class GeneticAbilitiesSystem
{
    [Dependency] private readonly SharedScaleVisualsSystem _scale = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly ReflectSystem _reflect = default!;

    private void InitializeDeflection()
    {
        SubscribeLocalEvent<GeneticAbilityStateComponent, ProjectileReflectAttemptEvent>(OnReflectProjectile,
            after: new[] { typeof(ReflectSystem) });
        SubscribeLocalEvent<GeneticAbilityStateComponent, HitScanReflectAttemptEvent>(OnReflectHitscan,
            after: new[] { typeof(ReflectSystem) });
    }

    private void UpdateGeneticSize(Entity<GeneticAbilityStateComponent> ent, float multiplier)
    {
        if (TerminatingOrDeleted(ent) || multiplier <= 0 || !float.IsFinite(multiplier) ||
            ent.Comp.AppliedSizeMultiplier == multiplier)
            return;
        var factor = multiplier / ent.Comp.AppliedSizeMultiplier;
        ent.Comp.AppliedSizeMultiplier = multiplier;
        var relative = !TryComp<ScaleVisualsComponent>(ent, out var visuals) || visuals.RelativeToOriginal;
        _scale.SetSpriteScale(ent.Owner, _scale.GetSpriteScale(ent.Owner) * factor, relative);
        _physics.ScaleFixtures(ent.Owner, factor);
    }

    private void UpdateDeflection(Entity<GeneticAbilityStateComponent> ent)
    {
        if (!HasAbility(ent, GeneticAbility.Deflection))
        {
            RemoveDeflector(ent);
            return;
        }
        if (ent.Comp.Deflector is not { } reflector || TerminatingOrDeleted(reflector))
            ent.Comp.Deflector = Spawn(ent.Comp.DeflectorPrototype, new EntityCoordinates(ent.Owner, Vector2.Zero));
    }

    private void RemoveDeflector(Entity<GeneticAbilityStateComponent> ent)
    {
        if (ent.Comp.Deflector is { } reflector && !TerminatingOrDeleted(reflector))
            QueueDel(reflector);
        ent.Comp.Deflector = null;
    }

    private void OnReflectProjectile(Entity<GeneticAbilityStateComponent> ent, ref ProjectileReflectAttemptEvent args)
    {
        if (args.Cancelled || !HasAbility(ent, GeneticAbility.Deflection) ||
            ent.Comp.Deflector is not { } reflector || TerminatingOrDeleted(reflector))
            return;
        args.Cancelled = _reflect.TryReflectProjectile(ent.Owner, reflector, args.ProjUid, args.Component);
    }

    private void OnReflectHitscan(Entity<GeneticAbilityStateComponent> ent, ref HitScanReflectAttemptEvent args)
    {
        if (args.Reflected || !HasAbility(ent, GeneticAbility.Deflection) ||
            ent.Comp.Deflector is not { } reflector || TerminatingOrDeleted(reflector) ||
            !TryComp<ReflectComponent>(reflector, out var reflect) || (reflect.Reflects & args.Reflective) == 0)
            return;
        if (_reflect.TryReflectHitscan(ent.Owner, reflector, args.Shooter, args.SourceItem, args.Direction, args.Damage, out var direction))
        {
            args.Direction = direction.Value;
            args.Reflected = true;
        }
    }
}
