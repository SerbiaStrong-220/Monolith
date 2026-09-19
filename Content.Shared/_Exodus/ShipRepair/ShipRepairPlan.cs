using System.Numerics;
using Content.Shared.Damage;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.ShipRepair;

/// <summary>A stable address within one revision of a repair snapshot.</summary>
public readonly record struct ShipRepairTarget(Vector2i Tile, int? EntityId = null);

public enum ShipRepairOperation : byte
{
    Tile,
    Restore,
    Heal,
}

public enum ShipRepairStage : byte
{
    Floor,
    Underfloor,
    Power,
    Structure,
}

/// <summary>
/// One quoted operation. Healing records the original entity and damage, so a delayed repair
/// cannot heal a replacement or damage received after the work started.
/// </summary>
public sealed class ShipRepairWork
{
    public required ShipRepairTarget Target;
    public required ShipRepairOperation Operation;
    public required Vector2 Position;
    public required TimeSpan Duration;
    public required int Cost;
    public EntityUid? Original;
    public DamageSpecifier? Damage;
    public EntProtoId? Prototype;
    public Angle Rotation;
    public int? TileType;
    public ShipRepairStage Stage;
    public bool Underfloor;
    public Angle? WallMountArc;
    public Angle WallMountDirection;
}

/// <summary>
/// Fixed batch shared by autonomous and future handheld area repair tools.
/// Runtime data: callers discard it when the grid or snapshot revision changes.
/// </summary>
public sealed class ShipRepairPlan
{
    public required EntityUid Grid;
    public required int Revision;
    public readonly List<ShipRepairWork> Work = new();
}
