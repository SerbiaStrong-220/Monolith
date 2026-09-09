using Content.Shared.Database._Exodus.Chat; // SS220 chat bans
using System.Net;
using Content.Shared.Database;
using Content.Shared.Eui;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Administration;

[Serializable, NetSerializable]
public sealed class BanPanelEuiState : EuiStateBase
{
    public string PlayerName { get; set; }
    public bool HasBan { get; set; }
    public bool Busy { get; set; } // Exodus chat ban feedback
    public string? Error { get; set; } // Exodus chat ban feedback

    public BanPanelEuiState(string playerName, bool hasBan)
    {
        PlayerName = playerName;
        HasBan = hasBan;
    }
}

public static class BanPanelEuiStateMsg
{
    [Serializable, NetSerializable]
    public sealed class CreateBanRequest(Ban ban) : EuiMessageBase
    {
        public Ban Ban { get; } = ban;
    }

    [Serializable, NetSerializable]
    public sealed class GetPlayerInfoRequest : EuiMessageBase
    {
        public string PlayerUsername { get; set; }

        public GetPlayerInfoRequest(string username)
        {
            PlayerUsername = username;
        }
    }
}

/// <summary>
///     Contains all the data related to a particular ban action created by the BanPanel window.
/// </summary>
[Serializable, NetSerializable]
public sealed record Ban
{
    public Ban(
        string? target,
        (IPAddress, int)? ipAddressTuple,
        bool useLastIp,
        ImmutableTypedHwid? hwid,
        bool useLastHwid,
        uint banDurationMinutes,
        string reason,
        NoteSeverity severity,
        ProtoId<JobPrototype>[]? bannedJobs,
        ProtoId<AntagPrototype>[]? bannedAntags,
        bool erase,
        BannableChats[]? bannedChats = null, // SS220 chat bans
        BanType? type = null) // SS220 chat bans
    {
        Target = target;
        IpAddress = ipAddressTuple?.Item1.ToString();
        IpAddressHid = ipAddressTuple?.Item2.ToString() ?? "0";
        UseLastIp = useLastIp;
        Hwid = hwid;
        UseLastHwid = useLastHwid;
        BanDurationMinutes = banDurationMinutes;
        Reason = reason;
        Severity = severity;
        BannedJobs = bannedJobs;
        BannedAntags = bannedAntags;
        Erase = erase;
        BannedChats = bannedChats; // SS220 chat bans
        Type = type ?? (bannedJobs?.Length > 0 || bannedAntags?.Length > 0 ? BanType.Role : BanType.Server); // SS220 chat bans
    }

    public readonly string? Target;
    public readonly string? IpAddress;
    public readonly string? IpAddressHid;
    public readonly bool UseLastIp;
    public readonly ImmutableTypedHwid? Hwid;
    public readonly bool UseLastHwid;
    public readonly uint BanDurationMinutes;
    public readonly string Reason;
    public readonly NoteSeverity Severity;
    public readonly ProtoId<JobPrototype>[]? BannedJobs;
    public readonly ProtoId<AntagPrototype>[]? BannedAntags;
    public readonly bool Erase;
    public readonly BannableChats[]? BannedChats; // SS220 chat bans
    public readonly BanType Type; // SS220 chat bans
}
