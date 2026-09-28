using Content.Server._Exodus.War;
using Content.Shared._Exodus.Territory;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Radio;

/// <summary>
/// Shares configured faction channels across direct alliances using the current sector diplomacy state.
/// </summary>
public sealed partial class FactionAllianceRadioSystem : EntitySystem
{
    [Dependency] private FactionWarSystem _wars = default!;
    [Dependency] private IPrototypeManager _prototype = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<GetRadioReceiveChannelsEvent>(OnGetReceiveChannels);
    }

    private void OnGetReceiveChannels(ref GetRadioReceiveChannelsEvent args)
    {
        if (!_wars.TryGetState(out var state))
            return;

        foreach (var alliance in state.Comp.Alliances)
        {
            if (alliance.FirstFaction == alliance.SecondFaction ||
                !state.Comp.Factions.Contains(alliance.FirstFaction) ||
                !state.Comp.Factions.Contains(alliance.SecondFaction) ||
                !_prototype.TryIndex(alliance.FirstFaction, out var first) ||
                !_prototype.TryIndex(alliance.SecondFaction, out var second))
            {
                continue;
            }

            var fromFirst = first.AllianceRadioChannels.Contains(args.Channel.ID);
            var fromSecond = second.AllianceRadioChannels.Contains(args.Channel.ID);
            if ((!fromFirst && !fromSecond) ||
                _wars.TryGetDeclaration(state, alliance.FirstFaction, alliance.SecondFaction, out _))
            {
                continue;
            }

            // Always match the original channel: a common ally must never relay between non-allied factions.
            if (fromFirst)
                AddChannels(second, ref args);

            if (fromSecond)
                AddChannels(first, ref args);
        }
    }

    private void AddChannels(TerritoryFactionPrototype faction, ref GetRadioReceiveChannelsEvent args)
    {
        foreach (var channelId in faction.AllianceRadioChannels)
        {
            if (channelId == args.Channel.ID || !_prototype.TryIndex(channelId, out var channel))
                continue;

            args.AdditionalChannels ??= new();
            args.AdditionalChannels.Add(channel);
        }
    }
}
