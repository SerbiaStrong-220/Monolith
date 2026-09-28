// © SS220, An EULA/CLA with a hosting restriction, full text: https://raw.githubusercontent.com/SerbiaStrong-220/space-station-14/master/CLA.txt

using Robust.Shared.Prototypes;

using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Exodus.Virology.Behaviors;

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class VirusSchizophreniaComponent : Component
{
    /// <summary>Prevents replaying initialization over the state restored from a saved host.</summary>
    [DataField]
    public bool StateApplied;

    /// <summary>Pool of self-chat lines this symptom picks from.</summary>
    [DataField]
    public ProtoId<VirusMessagePoolPrototype> Pool = "SchizophreniaThoughts";

    /// <summary>Random cd between messages.</summary>
    [DataField]
    public TimeSpan MinInterval = TimeSpan.FromSeconds(20);

    [DataField]
    public TimeSpan MaxInterval = TimeSpan.FromSeconds(55);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextMessageTime;

    /// <summary>No same line picked twice in a row.</summary>
    [DataField]
    public int LastIndex = -1;
}
