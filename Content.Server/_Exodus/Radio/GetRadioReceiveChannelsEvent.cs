using Content.Shared.Radio;

namespace Content.Server._Exodus.Radio;

/// <summary>
/// Raised once per transmission to allow reception through additional channels.
/// Does not rebroadcast the message, change its original channel or grant permission to transmit on another channel.
/// </summary>
[ByRefEvent]
public record struct GetRadioReceiveChannelsEvent(RadioChannelPrototype Channel)
{
    /// <summary>
    /// Additional receiver channels, allocated only when sharing is available.
    /// </summary>
    public HashSet<RadioChannelPrototype>? AdditionalChannels;
}
