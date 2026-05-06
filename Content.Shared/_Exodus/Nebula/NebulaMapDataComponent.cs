using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Nebula;

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
}
