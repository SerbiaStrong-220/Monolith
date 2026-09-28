// (c) Space Exodus Team - EXDS-RL with CLA

using Content.Shared.SS220.Discord;
using Robust.Client.State;
using Robust.Shared.Network;

namespace Content.Client.SS220.Discord;

public sealed partial class DiscordPlayerInfoManager // Exodus: generated dependency injection.
{
    [Dependency] private IClientNetManager _netMgr = default!; // Exodus: generated dependency injection.
    [Dependency] private IStateManager _stateManager = default!; // Exodus: generated dependency injection.

    private DiscordSponsorInfo? _info;

    public event Action? SponsorStatusChanged;

    public string AuthUrl { get; private set; } = string.Empty;

    public void Initialize()
    {
        _netMgr.RegisterNetMessage<MsgUpdatePlayerDiscordStatus>(UpdateSponsorStatus);
        _netMgr.RegisterNetMessage<MsgDiscordLinkRequired>(OnDiscordLinkRequired);
        _netMgr.RegisterNetMessage<MsgRecheckDiscordLink>();
    }

    private void UpdateSponsorStatus(MsgUpdatePlayerDiscordStatus message)
    {
        _info = message.Info;

        SponsorStatusChanged?.Invoke();
    }

    public SponsorTier[] GetSponsorTier()
    {
        return _info?.Tiers ?? [];
    }

    private void OnDiscordLinkRequired(MsgDiscordLinkRequired msg)
    {
        if (_stateManager.CurrentState is DiscordLinkRequiredState)
            return;

        AuthUrl = msg.AuthUrl;
        _stateManager.RequestStateChange<DiscordLinkRequiredState>();
    }
}
