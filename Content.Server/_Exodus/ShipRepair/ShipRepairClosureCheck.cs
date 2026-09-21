using System.Numerics;
using Content.Shared._Exodus.ShipRepair;
using Robust.Shared.Physics.Collision.Shapes;

namespace Content.Server._Exodus.ShipRepair;

/// <summary>Incremental before/after access comparison. Only data; never spawns speculative entities.</summary>
public sealed class ShipRepairClosureCheck
{
    public required ShipRepairPlan Plan;
    public int Revision;
    public int WorkRevision;
    public Box2 Bounds;
    public bool Expanded;
    public int Phase;
    public Vector2 Origin;
    public Vector2i Start;
    public Vector2i Scan;
    public Vector2i ScanMin;
    public Vector2i ScanMax;
    public int EntryIndex;
    public bool Approved;
    public bool Blocked;
    /// <summary>Wall-only plans do not need to prove access to every unrelated queued target.</summary>
    public bool SkipQueuedTargets;
    public HashSet<Vector2i>.Enumerator Boundary;
    public readonly List<ShipRepairClosureShape> Shapes = new();
    public readonly List<ShipRepairWork> Order = new();
    public readonly Queue<Vector2i> Frontier = new();
    public readonly HashSet<Vector2i> Before = new();
    public readonly HashSet<Vector2i> After = new();
    public readonly Dictionary<(Vector2i From, Vector2i To), bool> Edges = new();
}

public readonly record struct ShipRepairClosureShape(ShipRepairWork Work, IPhysShape Shape, bool BlocksMovement, bool BlocksRay);
