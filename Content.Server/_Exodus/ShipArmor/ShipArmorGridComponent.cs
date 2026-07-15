// (c) Space Exodus Team
using Robust.Shared.Maths;
using System.Numerics;

namespace Content.Server._Exodus.ShipArmor;

/// <summary>
/// Server-only spatial index of anchored ship armor blocks on a grid.
/// Present only while at least one armor is registered — damage hot path early-outs via HasComp.
/// </summary>
[RegisterComponent]
public sealed partial class ShipArmorGridComponent : Component
{
    /// <summary>
    /// Anchored armor entities on this grid and their spatial index entries.
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<EntityUid, ShipArmorIndexEntry> Armors = new();

    /// <summary>
    /// Spatial buckets containing armor entities by local grid position.
    /// </summary>
    [ViewVariables]
    public readonly Dictionary<Vector2i, HashSet<EntityUid>> Buckets = new();

    /// <summary>
    /// Largest radius of the currently registered armor on this grid.
    /// </summary>
    [ViewVariables]
    public float MaxRadius;

    /// <summary>
    /// Whether the cached protection bounds are valid.
    /// </summary>
    [ViewVariables]
    public bool HasProtectionBounds;

    /// <summary>
    /// Local-space minimum corner of the combined armor protection bounds.
    /// </summary>
    [ViewVariables]
    public Vector2 ProtectionBoundsMin;

    /// <summary>
    /// Local-space maximum corner of the combined armor protection bounds.
    /// </summary>
    [ViewVariables]
    public Vector2 ProtectionBoundsMax;
}

public readonly record struct ShipArmorIndexEntry(Vector2 LocalPosition, Vector2i Bucket);
