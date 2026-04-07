// (c) Space Exodus Team - EXDS-RL with CLA

using Robust.Shared.Serialization;

namespace Content.Shared.SS220.Discord;

[Serializable, NetSerializable]
public sealed class DiscordSponsorInfo
{
    public SponsorTier[] Tiers { get; set; } = [];
}
