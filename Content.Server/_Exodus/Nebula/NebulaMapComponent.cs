using Content.Shared._Exodus.Nebula;

namespace Content.Server._Exodus.Nebula;

/// <summary>
/// Stores generated Exodus space nebulas for a map.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaMapComponent : Component
{
    [ViewVariables]
    public int Seed;

    [ViewVariables]
    public List<NebulaShape> Nebulas = new();

    [ViewVariables]
    public List<EntityUid> NebulaMarkers = new();

    [ViewVariables]
    public List<NebulaProtectedArea> ProtectedAreas = new();

    [ViewVariables]
    public NebulaGenerationRejections Rejections;

    [ViewVariables]
    public int Attempts;

    [ViewVariables]
    public int RequestedCount;

    [ViewVariables]
    public bool Complete;
}
