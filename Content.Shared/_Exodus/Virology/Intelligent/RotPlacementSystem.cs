using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Physics.Components;

namespace Content.Shared._Exodus.Virology.Intelligent;

/// <summary>Occupancy rules shared by the construction preview and authoritative validation.</summary>
public sealed partial class RotPlacementSystem : EntitySystem
{
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    private EntityQuery<RotColonyMemberComponent> _members;
    private EntityQuery<MobStateComponent> _mobStates;
    private EntityQuery<PhysicsComponent> _physics;

    public override void Initialize()
    {
        base.Initialize();
        _members = GetEntityQuery<RotColonyMemberComponent>();
        _mobStates = GetEntityQuery<MobStateComponent>();
        _physics = GetEntityQuery<PhysicsComponent>();
    }

    public RotOccupant Classify(Entity<RotIntelligentComponent> core, RotBuildingPrototype recipe, EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || _containers.IsEntityInContainer(uid))
            return RotOccupant.Clear;
        var convertible = recipe.Wall && _whitelist.IsValid(core.Comp.ConvertibleWalls, uid);
        if (_members.TryComp(uid, out var member))
        {
            if (member.Core != null && member.Core != core.Owner)
                return RotOccupant.OtherColony;
            if (member.Tissue)
                return RotOccupant.Tissue;
            if (member.Size != Vector2i.Zero && !convertible)
                return RotOccupant.Blocked;
        }
        if (!_physics.TryComp(uid, out var physics) || !physics.CanCollide || !physics.Hard)
            return RotOccupant.Clear;
        if (_mobStates.TryComp(uid, out var mob) && !_mobs.IsDead(uid, mob))
            return RotOccupant.Blocked;
        if ((physics.CollisionLayer & (int)CollisionGroup.FullTileMask) == 0)
            return RotOccupant.Clear;
        return convertible ? RotOccupant.ConvertibleWall : RotOccupant.Blocked;
    }
}

public enum RotOccupant : byte
{
    Clear,
    Tissue,
    Blocked,
    OtherColony,
    ConvertibleWall,
}
