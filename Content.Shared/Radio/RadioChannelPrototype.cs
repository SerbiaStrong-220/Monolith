using Content.Shared.FixedPoint;
using Content.Shared.SS220.TTS;
using Robust.Shared.Prototypes;

namespace Content.Shared.Radio;

[Prototype]
public sealed partial class RadioChannelPrototype : IHearableChannelPrototype //ss220 add telepathy mute for ghosts
{
    /// <summary>
    /// Human-readable name for the channel.
    /// </summary>
    [DataField("name")]
    public LocId Name { get; private set; } = string.Empty;

    [ViewVariables(VVAccess.ReadOnly)]
    public string LocalizedName => Loc.GetString(Name);

    /// <summary>
    /// Single-character prefix to determine what channel a message should be sent to.
    /// </summary>
    [DataField("keycode")]
    public char KeyCode { get; private set; } = '\0';

    [DataField("frequency")]
    public int Frequency { get; private set; } = 0;

    // SS220-radio-headset-begin
    [DataField]
    public FixedPoint2 MinFrequency { get; private set; } = 0;

    [DataField]
    public FixedPoint2 MaxFrequency { get; private set; } = 0;

    /// <summary>
    /// Defines when to use frequency logic for this channel
    /// </summary>
    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public bool FrequencyRadio = false;

    [DataField]
    [ViewVariables(VVAccess.ReadOnly)]
    public LocId FrequencyChanelName = "base-frequency-channel-name";
    // SS220-radio-headset-end

    [DataField("color")]
    public Color Color { get; private set; } = Color.Lime;

    [IdDataField, ViewVariables]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// If channel is long range it doesn't require telecommunication server
    /// and messages can be sent across different stations
    /// </summary>
    [DataField("longRange"), ViewVariables]
    public bool LongRange = false;

    // Frontier: radio channel frequencies
    /// <summary>
    /// If true, the frequency of the message being sent will be appended to the chat message
    /// </summary>
    [DataField, ViewVariables]
    public bool ShowFrequency = false;
    // End Frontier
    
    /// <summary>
    /// Maximum distance in meters this channel can transmit. If 0 or null, range is unlimited except by map boundaries.
    /// </summary>
    [DataField("maxRange"), ViewVariables]
    public float? MaxRange = null;
}
