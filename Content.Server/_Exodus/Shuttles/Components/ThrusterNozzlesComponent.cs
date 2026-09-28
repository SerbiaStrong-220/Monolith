using System.Numerics;
using Content.Server.Shuttles.Systems;
using Content.Shared._Exodus.Shuttles;

namespace Content.Server._Exodus.Shuttles.Components;

/// <summary>
/// Configurable, independently exposed nozzles handled by the standard thruster system.
/// Each available direction supplies the parent thruster's full thrust.
/// </summary>
[RegisterComponent, Access(typeof(ThrusterSystem))]
public sealed partial class ThrusterNozzlesComponent : Component
{
    [DataField(required: true)]
    public Dictionary<ThrusterNozzleDirection, ThrusterNozzle> Nozzles = new();

    /// <summary>The anchored grid watching exposure changes, including while blocked or unpowered.</summary>
    [ViewVariables]
    public EntityUid? WatchedGrid;

    /// <summary>Registration snapshot, so rotation, reparenting and part changes remove the old contribution.</summary>
    [ViewVariables]
    public EntityUid? RegisteredGrid;

    [ViewVariables]
    public DirectionFlag RegisteredDirections;

    [ViewVariables]
    public DirectionFlag AvailableNozzles;

    [ViewVariables]
    public DirectionFlag FiringNozzles;

    [ViewVariables]
    public float RegisteredThrust;

    [ViewVariables]
    public float RegisteredBaseThrust;
}

[DataDefinition]
public sealed partial class ThrusterNozzle
{
    /// <summary>
    /// All these positions must be space. Coordinates are relative to the engine's origin,
    /// with this nozzle facing +Y, before applying nozzle and engine rotations.
    /// </summary>
    [DataField]
    public List<Vector2> SpaceCheckOffsets = [Vector2.UnitY];

    /// <summary>Entities inside this nozzle's burn fixture; damage is applied only while it fires.</summary>
    public readonly HashSet<EntityUid> Colliding = new();
}
