using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Cargo;

/// <summary>
/// Packaging policy for orders delivered by a station's cargo terminals.
/// </summary>
[DataDefinition]
public sealed partial class CargoOrderPackagingSettings
{
    /// <summary>
    /// Minimum original order quantity for bulk packaging. Zero disables bulk packaging; structures still require a crate.
    /// </summary>
    [DataField]
    public int MinimumQuantity = 10;

    /// <summary>
    /// Empty entity-storage crate used for bulk shipments. Its capacity limits each package.
    /// </summary>
    [DataField]
    public EntProtoId CratePrototype = "ExodusCargoDeliveryCrate";
}
