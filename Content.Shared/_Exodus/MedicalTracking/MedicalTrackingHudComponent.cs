using Content.Shared.StatusIcon;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.MedicalTracking;

/// <summary>Public medical service insignia on a body, visible through a medical HUD.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MedicalTrackingHudComponent : Component
{
    /// <summary>Animated border drawn around the body's existing health status icon.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<HealthIconPrototype>? Border;
}
