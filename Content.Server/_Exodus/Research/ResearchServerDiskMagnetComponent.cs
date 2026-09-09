using Content.Shared.Whitelist;

namespace Content.Server._Exodus.Research;

/// <summary>
/// Periodically offers nearby whitelisted entities to an R&amp;D server for insertion.
/// </summary>
[RegisterComponent]
public sealed partial class ResearchServerDiskMagnetComponent : Component
{
    /// <summary>
    /// Maximum pickup range in tiles.
    /// </summary>
    [DataField]
    public float Range = 1.5f;

    /// <summary>
    /// Delay between pickup scans.
    /// </summary>
    [DataField]
    public TimeSpan ScanInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Maximum number of entities that may be inserted during a single scan.
    /// </summary>
    [DataField]
    public int MaxEntitiesPerScan = 15;

    /// <summary>
    /// Whether the receiver must have a powered APC connection to scan.
    /// </summary>
    [DataField]
    public bool RequiresPower = true;

    /// <summary>
    /// Whether candidates must be resting on the ground.
    /// </summary>
    [DataField]
    public bool OnlyOnGround = true;

    /// <summary>
    /// Entities eligible for an insertion attempt. Null accepts every nearby entity.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Whether the magnet is currently enabled.
    /// </summary>
    [DataField]
    public bool MagnetEnabled;

    /// <summary>
    /// Absolute time of the next scan. Runtime state only.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public TimeSpan NextScan;
}
