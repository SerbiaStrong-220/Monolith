// (c) Space Exodus Team - EXDS-RL with CLA
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Content.Server.Database._Exodus.Economy;

/// <summary>
/// Persistent administrative market settings, independent from the saved market quotes.
/// The settings payload carries its own schema version and is validated by the economy system.
/// </summary>
[Table("economy_market_settings")]
public sealed class MarketSettingsRecord
{
    public const int SingletonId = 1;

    [Key, Column("id"), DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = SingletonId;

    /// <summary>
    /// Monotonically increasing revision used for atomic compare-and-swap writes.
    /// </summary>
    public long Revision { get; set; }

    [Required]
    public string Settings { get; set; } = string.Empty;
}
