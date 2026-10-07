// Exodus: configurable packaging of bulk cargo orders.
using Content.Server._Exodus.Cargo;

namespace Content.Server.Cargo.Components;

public sealed partial class StationCargoOrderDatabaseComponent
{
    /// <summary>
    /// Bulk shipment threshold and container used by this station's cargo deliveries.
    /// </summary>
    [DataField]
    public CargoOrderPackagingSettings BulkPackaging = new();
}
