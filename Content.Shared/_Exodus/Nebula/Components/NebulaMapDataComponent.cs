using Content.Shared._Exodus.Nebula.Generation;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Components;

/// <summary>
/// Networked snapshot of all nebulas on a map. Lives on the map entity and lets the client
/// answer FTL/parallax/visual questions without depending on radar visualization data.
/// Replaces the old <c>NebulaFTLDataComponent</c> and now carries pre-baked per-effect flags.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NebulaMapDataComponent : Component
{
    [AutoNetworkedField, ViewVariables]
    public List<NebulaSummary> Nebulas = new();

    [AutoNetworkedField, ViewVariables]
    public List<NebulaRadarBlipSummary> RadarBlips = new();

    [AutoNetworkedField, ViewVariables]
    public WorldEndNebulaShape WorldEnd;

    /// <summary>Marker prototype for the inner concentric sub-zone (boundary..MidRadius).</summary>
    [AutoNetworkedField, ViewVariables]
    public EntProtoId WorldEndInnerMarker;

    /// <summary>Marker prototype for the outer concentric sub-zone (MidRadius..∞).</summary>
    [AutoNetworkedField, ViewVariables]
    public EntProtoId WorldEndOuterMarker;
}
