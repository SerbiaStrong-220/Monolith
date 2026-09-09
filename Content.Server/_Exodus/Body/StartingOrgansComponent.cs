using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Body;

/// <summary>
/// Installs organs in existing body slots on map initialization, replacing their previous contents.
/// </summary>
[RegisterComponent]
public sealed partial class StartingOrgansComponent : Component
{
    /// <summary>
    /// Body organ slot IDs and the organ prototypes to install in them.
    /// Empty slots are filled; body parts and unrelated organs are preserved.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<string, EntProtoId> Organs = new();
}
