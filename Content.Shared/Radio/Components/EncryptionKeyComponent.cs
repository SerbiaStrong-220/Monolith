using Content.Shared.Chat;
using Robust.Shared.Prototypes; // Exodus
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Set;

namespace Content.Shared.Radio.Components;

/// <summary>
///     This component is currently used for providing access to channels for "HeadsetComponent"s.
///     It should be used for intercoms and other radios in future.
/// </summary>
[RegisterComponent]
public sealed partial class EncryptionKeyComponent : Component
{
    [DataField]
    public HashSet<RadioChannelEntry> Channels = new(); // Exodus

    /// <summary>
    ///     This is the channel that will be used when using the default/department prefix (<see cref="SharedChatSystem.DefaultChannelKey"/>).
    /// </summary>
    [DataField("defaultChannel", customTypeSerializer: typeof(PrototypeIdSerializer<RadioChannelPrototype>))]
    public string? DefaultChannel;
}

// Exodus-Begin
[DataDefinition]
public partial record struct RadioChannelEntry
{
    [DataField(required: true)]
    public ProtoId<RadioChannelPrototype> Channel;
    [DataField]
    public bool CanSpeak = true;

    public RadioChannelEntry(ProtoId<RadioChannelPrototype> channel, bool canSpeak = true)
    {
        Channel = channel;
        CanSpeak = canSpeak;
    }
}
// Exodus-End
