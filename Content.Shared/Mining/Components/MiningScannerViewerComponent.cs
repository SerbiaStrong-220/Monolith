// Exodus-MiningScannerRefactor
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization;

namespace Content.Shared.Mining.Components;

[RegisterComponent, NetworkedComponent, Access(typeof(MiningScannerViewerSystem)), AutoGenerateComponentState(true)]
public sealed partial class MiningScannerViewerComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<MiningScannerRecord> Records = new();
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class MiningScannerRecord
{
    [DataField]
    public MapCoordinates PingLocation;

    [DataField]
    public float ViewRange;

    [DataField]
    public TimeSpan CreatedAt;

    [DataField]
    public TimeSpan AnimationDuration = TimeSpan.FromSeconds(1.5f);

    /// <summary>
    /// How long ores should be showing minus the duration of the animation
    /// </summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(3.5f);
}

[Serializable, NetSerializable]
public sealed partial class MiningScannerViewerComponentState : ComponentState
{
    [DataField]
    public List<MiningScannerRecord> Records = new();
}
