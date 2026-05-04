// (c) Space Exodus Team - EXDS-RL with CLA
// Authors: Provstat
using Content.Server.Explosion.EntitySystems;
using Content.Shared._Exodus.Examine.Damage;
using Content.Shared.Damage;
using Content.Shared.Damage.Events;
using Content.Shared.Explosion;
using Content.Shared.Explosion.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Examine;

public sealed class ExplosiveEntityExamineSystem : EntitySystem
{
    [Dependency] private readonly ExplosionExamineSystem _explosionExamine = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    public ExamineExplosionInfo? GetExplosiveInfo(ExplosiveComponent comp)
    {
        var damage = GetExplosionDamage(comp.ExplosionType);

        return new()
        {
            Damage = damage,
            TotalIntensity = comp.TotalIntensity,
            IntensitySlope = comp.IntensitySlope,
            MaxIntensity = comp.MaxIntensity,
        };
    }

    public ExamineExplosionInfo? GetExplosiveInfo(string proto)
    {
        if (!_prototypeManager.TryIndex<EntityPrototype>(proto, out var entityProto))
            return null;

        return GetExplosiveInfo(entityProto);
    }

    public ExamineExplosionInfo? GetExplosiveInfo(EntityPrototype entityProto)
    {
        if (entityProto.Components
            .TryGetValue(Factory.GetComponentName<ExplosiveComponent>(), out var explosive))
        {
            var p = (ExplosiveComponent)explosive.Component;
            return GetExplosiveInfo(p);
        }

        return null;
    }

    public DamageSpecifier? GetExplosionDamage(string explosionTypeProto)
    {
        if (!_prototypeManager.TryIndex<ExplosionPrototype>(explosionTypeProto, out var entityProto))
            return null;

        return entityProto.DamagePerIntensity;
    }
}
