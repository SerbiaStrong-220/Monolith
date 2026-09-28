using Content.Shared.Silicons.StationAi;
using Robust.Shared.GameStates;

namespace Content.Shared.StationAi;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, Access(typeof(SharedStationAiSystem))]
public sealed partial class StationAiVisionComponent : Component
{
    // Exodus: independent organic camera networks never grant station AI access.
    [DataField, AutoNetworkedField]
    public EntityUid? Network;

    [DataField, AutoNetworkedField]
    public bool Enabled = true;

    [DataField, AutoNetworkedField]
    public bool Occluded = true;

    /// <summary>
    /// Range in tiles
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Range = 7.5f;
}
