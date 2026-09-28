using System.Numerics;
using Content.Server.Destructible;
using Content.Shared._Exodus.Virology.Lifecycle;
using Content.Shared.Damage;
using Content.Shared.Weapons.Melee;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;

namespace Content.Server._Exodus.Virology.Lifecycle;

/// <summary>Shared tile-based structural attack for rot creatures, including rotated grids.</summary>
public sealed partial class RotGroundStrikeSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    private readonly HashSet<Entity<DestructibleComponent>> _obstacles = [];
    private EntityQuery<PhysicsComponent> _physics;
    private EntityQuery<RotCreatureComponent> _rot;

    public override void Initialize()
    {
        base.Initialize();
        _physics = GetEntityQuery<PhysicsComponent>();
        _rot = GetEntityQuery<RotCreatureComponent>();
    }

    private bool Gather(EntityUid uid, int radius)
    {
        _obstacles.Clear();
        if (Transform(uid).GridUid is not { } grid || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return false;
        var tile = _map.TileIndicesFor(grid, mapGrid, Transform(uid).Coordinates);
        var minimum = new Vector2(tile.X - radius, tile.Y - radius) * mapGrid.TileSize;
        _lookup.GetLocalEntitiesIntersecting(grid,
            Box2.FromDimensions(minimum, new Vector2((2 * radius + 1) * mapGrid.TileSize)),
            _obstacles, LookupFlags.Uncontained);
        return true;
    }

    private bool InArea(EntityUid source, EntityUid target, int radius)
    {
        if (target == source || TerminatingOrDeleted(target) || _rot.HasComp(target)
            || Transform(source).GridUid is not { } grid || Transform(target).GridUid != grid
            || !TryComp<MapGridComponent>(grid, out var mapGrid))
            return false;
        var center = _map.TileIndicesFor(grid, mapGrid, Transform(source).Coordinates);
        var tile = _map.TileIndicesFor(grid, mapGrid, Transform(target).Coordinates);
        return Math.Abs(tile.X - center.X) <= radius && Math.Abs(tile.Y - center.Y) <= radius;
    }

    public bool HasObstacle(EntityUid uid, int radius)
    {
        if (!Gather(uid, radius) || !_physics.TryComp(uid, out var body))
            return false;
        foreach (var (obstacle, _) in _obstacles)
        {
            if (InArea(uid, obstacle, radius) && _physics.TryComp(obstacle, out var physics)
                && physics.Hard && physics.CanCollide
                && ((physics.CollisionLayer & body.CollisionMask) != 0 || (physics.CollisionMask & body.CollisionLayer) != 0))
                return true;
        }
        return false;
    }

    public bool TryStrike(EntityUid uid, int radius, DamageSpecifier damage, SoundSpecifier? sound)
    {
        if (!Gather(uid, radius) || !TryComp<MeleeWeaponComponent>(uid, out var weapon)
            || !_melee.AttemptHeavyAttack(uid, uid, weapon, [], new EntityCoordinates(uid, 0f, -0.3f)))
            return false;
        _audio.PlayPvs(sound, uid);
        foreach (var (obstacle, _) in _obstacles)
        {
            if (InArea(uid, obstacle, radius))
                _damage.TryChangeDamage(obstacle, damage, origin: uid);
        }
        return true;
    }
}
