using System;

namespace Content.Server.Database._Exodus.SafetyDepositBox;

/// <summary>
/// Administrative list projection for a persistent box, without serialized entity data.
/// </summary>
public sealed record SafetyDepositAdminSummary
{
    public Guid BoxId { get; init; }
    public Guid OwnerUserId { get; init; }
    public int CharacterIndex { get; init; }
    public string OwnerName { get; init; } = string.Empty;
    public string ProtoId { get; init; } = string.Empty;
    public string? Nickname { get; init; }
    public DateTime? LastWithdrawn { get; init; }
    public int? LastWithdrawnRoundId { get; init; }
    public int ItemCount { get; init; }
}
