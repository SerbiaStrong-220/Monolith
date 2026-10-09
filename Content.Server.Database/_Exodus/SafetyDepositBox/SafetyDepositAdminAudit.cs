using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Content.Server.Database._Exodus.SafetyDepositBox;

/// <summary>
/// Durable administrative operation journal. Identifiers deliberately have no foreign keys so
/// account, round, or box deletion cannot erase the audit or its recovery payload.
/// </summary>
[Table("safety_deposit_admin_audit")]
public sealed class SafetyDepositAdminAudit
{
    /// <summary>Caller-assigned operation identifier, also used to detect an uncertain commit.</summary>
    [Key, Column("id"), DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    /// <summary>Account that authorized the operation.</summary>
    public Guid AdminUserId { get; set; }

    /// <summary>Administrator account name at the time of the operation.</summary>
    [Required]
    public string AdminName { get; set; } = string.Empty;

    /// <summary>Box owner account at the time of the operation.</summary>
    public Guid OwnerUserId { get; set; }

    /// <summary>Owner character slot at the time of the operation.</summary>
    public int CharacterIndex { get; set; }

    /// <summary>Persistent external identifier of the affected box.</summary>
    public Guid BoxId { get; set; }

    /// <summary>UTC operation timestamp.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Administrative action identifier.</summary>
    [Required]
    public string Action { get; set; } = string.Empty;

    /// <summary>Operation outcome: pending, success, failed, or rolled-back.</summary>
    [Required]
    public string Result { get; set; } = string.Empty;

    /// <summary>Human-readable operation context and outcome.</summary>
    [Required]
    public string Details { get; set; } = string.Empty;

    /// <summary>Optional serialized entity YAML retained for recovery; never sent to the UI.</summary>
    public string? ItemData { get; set; }

    /// <summary>Informational round identifier, without a foreign key.</summary>
    public int? RoundId { get; set; }
}
