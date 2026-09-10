using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Exodus.Teleport;

/// <summary>
/// Floor panel: stand on it and activate to teleport to the nearest other shuttle on the map.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentPause]
public sealed partial class NearestShuttleTeleporterComponent : Component
{
    /// <summary>
    /// Max search range in world units. 0 = unlimited on the same map.
    /// </summary>
    [DataField]
    public float MaxRange = 512f;

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(60);

    [DataField]
    public TimeSpan FailureCooldown = TimeSpan.FromSeconds(2);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextUse;

    [DataField]
    public string PopupSuccess = "company-teleporter-success";

    [DataField]
    public string PopupStandOnPad = "company-teleporter-stand-on-pad";

    [DataField]
    public string PopupFail = "company-teleporter-fail";

    [DataField]
    public string PopupCooldown = "company-teleporter-cooldown";
}
