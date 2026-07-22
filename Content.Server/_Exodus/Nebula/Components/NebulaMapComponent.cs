using Content.Shared._Exodus.Nebula.Generation;
using Content.Shared._Exodus.Nebula.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Nebula.Components;

/// <summary>
/// Server-authoritative state for nebulas on a map: the math shapes, which marker prototype
/// each nebula uses, marker entities, and generation diagnostics. The networked summary
/// the client reads lives separately on <see cref="NebulaMapDataComponent"/>.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaMapComponent : Component
{
    [ViewVariables]
    public int Seed;

    [ViewVariables]
    public List<NebulaShape> Nebulas = new();

    [ViewVariables]
    public List<EntProtoId> NebulaPrototypes = new();

    [ViewVariables]
    public List<EntityUid> NebulaMarkers = new();

    [ViewVariables]
    public List<NebulaProtectedArea> ProtectedAreas = new();

    [ViewVariables]
    public NebulaGenerationRejections Rejections;

    [ViewVariables]
    public int Attempts;

    [ViewVariables]
    public int MaxAttempts;

    [ViewVariables]
    public double MaxTotalArea;

    [ViewVariables]
    public double TotalArea;

    [ViewVariables]
    public bool Complete;

    [ViewVariables]
    public TimeSpan NextMarkerValidation;

    [ViewVariables]
    public WorldEndNebulaShape WorldEnd;

    [ViewVariables]
    public EntProtoId WorldEndInnerMarker;

    [ViewVariables]
    public EntProtoId WorldEndOuterMarker;

    [ViewVariables]
    public Color WorldEndRadarColor;
}
