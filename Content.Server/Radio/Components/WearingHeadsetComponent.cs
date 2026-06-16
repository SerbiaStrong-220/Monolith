using Content.Server.Radio.EntitySystems;

namespace Content.Server.Radio.Components;

/// <summary>
///     This component is used to tag players that are currently wearing an ACTIVE headset.
/// </summary>
[RegisterComponent]
public sealed partial class WearingHeadsetComponent : Component
{
    // Exodus-begin: support multiple active headsets
    [DataField("headsets")]
    public List<EntityUid> Headsets = new();
    // Exodus-end
}
