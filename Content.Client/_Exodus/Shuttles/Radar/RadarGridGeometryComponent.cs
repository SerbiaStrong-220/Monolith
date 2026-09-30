using System.Numerics;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Shuttles.Radar;

/// <summary>
/// Client-only radar outline of a grid, shared by every mass scanner that draws it.
/// Geometry is kept per grid chunk in grid-local coordinates, so a changed tile rebuilds only its chunk.
/// </summary>
[RegisterComponent, Access(typeof(RadarGridGeometrySystem))]
public sealed partial class RadarGridGeometryComponent : Component
{
    /// <summary>Fill triangles and merged outline segments of each non-empty grid chunk.</summary>
    [ViewVariables]
    public readonly Dictionary<Vector2i, RadarGridChunkGeometry> Chunks = new();

    /// <summary>Chunks whose tiles or border neighbours changed since the last rebuild.</summary>
    [ViewVariables]
    public readonly HashSet<Vector2i> DirtyChunks = new();

    /// <summary>Radar edge markers parented to this grid.</summary>
    [ViewVariables]
    public readonly HashSet<EntityUid> Markers = new();

    /// <summary>Outline segments contributed by <see cref="Markers"/>, in grid-local coordinates.</summary>
    public readonly List<Vector2> MarkerEdges = new();

    /// <summary>All chunk fill triangles, concatenated for a single draw call.</summary>
    public Vector2[] Fill = Array.Empty<Vector2>();

    [ViewVariables]
    public int FillCount;

    /// <summary>All chunk outline segments and marker edges, concatenated for a single draw call.</summary>
    public Vector2[] Edges = Array.Empty<Vector2>();

    [ViewVariables]
    public int EdgeCount;

    /// <summary>Set when the tile data cannot be trusted incrementally, e.g. before the first build.</summary>
    [ViewVariables]
    public bool FullRebuild = true;

    [ViewVariables]
    public bool MarkersDirty = true;

    /// <summary>Whether the markers were collected at least once; afterwards they are tracked by events.</summary>
    [ViewVariables]
    public bool MarkersCollected;

    /// <summary>Whether tile-change events arrived since <see cref="LastTileModifiedTick"/> was recorded.</summary>
    [ViewVariables]
    public bool TileEventsSeen;

    [ViewVariables]
    public GameTick LastTileModifiedTick;

    /// <summary>Real time before which a dirty, already drawable outline is not rebuilt again.</summary>
    [ViewVariables]
    public TimeSpan NextRebuild;

    /// <summary>Real time of the last draw request, used to release caches of grids nobody looks at.</summary>
    [ViewVariables]
    public TimeSpan LastUsed;

    /// <summary>Number of completed builds. Tests and debugging use it to observe incremental updates.</summary>
    [ViewVariables]
    public int Version;
}

public sealed class RadarGridChunkGeometry
{
    public readonly List<Vector2> Fill = new();
    public readonly List<Vector2> Edges = new();
}
