using System.Collections.Generic;
using Robust.Shared.Maths;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Marker for a siphon whose parent grid has nebula presence and a working filter.
/// Only marked siphons are placed into the timed processing queue.
/// </summary>
[RegisterComponent]
public sealed partial class ActiveNebulaGasSiphonComponent : Component
{
    /// <summary>
    /// Whether the cached space-axis result is valid for the stored clearance bounds.
    /// </summary>
    public bool AxisCacheValid;

    /// <summary>
    /// Cached result of the siphon's space-axis check.
    /// </summary>
    public bool AxisClear;

    /// <summary>
    /// First tile in the cached space-axis bounds.
    /// </summary>
    public Vector2i ClearanceMin;

    /// <summary>
    /// Last tile in the cached space-axis bounds.
    /// </summary>
    public Vector2i ClearanceMax;

    /// <summary>
    /// Grid whose clearance buckets contain this siphon.
    /// </summary>
    public EntityUid? ClearanceGrid;

    /// <summary>
    /// Cached bucket keys used to remove this siphon from the grid index.
    /// </summary>
    public readonly List<Vector2i> ClearanceBucketKeys = new();

    /// <summary>
    /// Stable phase used to spread recurring siphon updates after the first immediate update.
    /// </summary>
    public TimeSpan PhaseOffset;

    /// <summary>
    /// Update interval used to calculate <see cref="PhaseOffset"/>.
    /// </summary>
    public TimeSpan PhaseInterval;

    /// <summary>
    /// Whether the phase offset has already been applied to the first recurring update.
    /// </summary>
    public bool PhaseApplied;

    /// <summary>
    /// Whether the phase values have been initialized for the current update interval.
    /// </summary>
    public bool PhaseInitialized;
}
