// Adapted from SS220 keen hearing. EULA/CLA: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Content.Shared.Actions;
using Robust.Shared.GameStates;

namespace Content.Shared._Exodus.Virology.Behaviors;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class KeenHearingComponent : Component
{
    [DataField, AutoNetworkedField]
    public bool Enabled;

    [DataField, AutoNetworkedField]
    public float VisionRadius = 5f;

    [DataField, AutoNetworkedField]
    public float HighSensitiveVisionRadius = 2f;

    [DataField]
    public bool ManualOn;

    [DataField, AutoPausedField]
    public TimeSpan? ToggleTime;
}

[ByRefEvent]
public record struct GetKeenHearingModifiersEvent
{
    public bool ForceOn;
}

public sealed partial class UseKeenHearingEvent : InstantActionEvent
{
    [DataField]
    public TimeSpan? Duration;
}
