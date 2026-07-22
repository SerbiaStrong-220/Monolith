using System.Collections.Generic;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula.Components;

/// <summary>
/// Runtime state for nebula EMP hazards on a grid. Static configuration is on the marker
/// prototype; this component only stores per-grid timers and statistics.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaEmpGridHazardComponent : Component
{
    /// <summary>Marker prototype id of the nebula causing the hazard.</summary>
    [ViewVariables]
    public EntProtoId Marker;

    [ViewVariables]
    public bool TimersInitialized;

    [ViewVariables]
    public TimeSpan NextPulse;

    [ViewVariables]
    public TimeSpan LastPulse;

    [ViewVariables]
    public TimeSpan LastPulseDelta;

    [ViewVariables]
    public int PulseCount;

    /// <summary>
    /// Cached grid tile indices that were valid EMP pulse candidates during the last full scan.
    /// Stored as grid indices because tile refs can become stale after topology changes or grid splits.
    /// </summary>
    [ViewVariables]
    public readonly List<Vector2i> CachedPulseTiles = new();

    [ViewVariables]
    public bool PulseTileCacheInitialized;

    [ViewVariables]
    public TimeSpan NextPulseTileCacheRefresh;
}
