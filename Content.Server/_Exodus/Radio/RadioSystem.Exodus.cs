using Content.Server.Radio.Components;
using Content.Shared.Ghost;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;

namespace Content.Server.Radio.EntitySystems;

public sealed partial class RadioSystem
{
    private EntityQuery<IntercomComponent> _receptionIntercomQuery;
    private EntityQuery<RadioMicrophoneComponent> _receptionMicrophoneQuery;
    private EntityQuery<GhostComponent> _receptionGhostQuery;

    private void InitializeReceptionChannels()
    {
        _receptionIntercomQuery = GetEntityQuery<IntercomComponent>();
        _receptionMicrophoneQuery = GetEntityQuery<RadioMicrophoneComponent>();
        _receptionGhostQuery = GetEntityQuery<GhostComponent>();
    }

    private bool CanReceiveChannel(
        Entity<ActiveRadioComponent> receiver,
        RadioChannelPrototype channel,
        int frequency,
        HashSet<RadioChannelPrototype>? additionalChannels)
    {
        // Most receivers do not listen to a given private channel; keep that rejection cheap.
        if (!receiver.Comp.ReceiveAllChannels && !receiver.Comp.Channels.Contains(channel.ID) && additionalChannels == null)
            return false;

        _receptionIntercomQuery.TryGetComponent(receiver, out var intercom);
        _receptionMicrophoneQuery.TryGetComponent(receiver, out var microphone);
        var ignoreFrequency = _receptionGhostQuery.HasComp(receiver);

        if ((receiver.Comp.ReceiveAllChannels || HasReceiveChannel(receiver.Comp, intercom, channel)) &&
            (ignoreFrequency || (microphone?.Frequency ?? channel.Frequency) == frequency))
        {
            return true;
        }

        // Preserve ghosts' explicit channel filters and only bridge the configured frequencies.
        if (ignoreFrequency || additionalChannels == null || frequency != channel.Frequency)
            return false;

        foreach (var additional in additionalChannels)
        {
            if (HasReceiveChannel(receiver.Comp, intercom, additional) &&
                (microphone == null || microphone.Frequency == additional.Frequency))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasReceiveChannel(
        ActiveRadioComponent radio,
        IntercomComponent? intercom,
        RadioChannelPrototype channel)
    {
        return radio.Channels.Contains(channel.ID) &&
               (intercom == null || intercom.SupportedChannels.Contains(channel.ID));
    }
}
