// (c) Space Exodus Team - EXDS-RL with CLA

using Content.Shared.SS220.Discord;
using Robust.Shared.Network;

namespace Content.Client.SS220.Discord;

public sealed partial class DiscordPlayerInfoManager
{
    [Dependency] private IClientNetManager _net = default!;

    private DiscordSponsorInfo? _info;

    public event Action? SponsorStatusChanged;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgUpdatePlayerDiscordStatus>(UpdateSponsorStatus);
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
}
