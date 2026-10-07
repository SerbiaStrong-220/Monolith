// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Economy;

/// <summary>
/// Live row on a cargo order console: static catalog product and/or resale stock from sold goods.
/// </summary>
[Serializable, NetSerializable]
public sealed class CargoMarketListing
{
    /// <summary>
    /// Catalog: <c>CargoProductPrototype.ID</c>.
    /// Resale: <c>resale:{EntityPrototype.ID}</c>.
    /// </summary>
    public string ProductId = string.Empty;

    /// <summary>Entity prototype to spawn (catalog product entity or resale entity).</summary>
    public EntProtoId EntityProtoId;

    /// <summary>Display name for UI.</summary>
    public string DisplayName = string.Empty;

    /// <summary>Category loc-id / key for sidebar filter (resale uses a fixed "resale" category).</summary>
    public string Category = string.Empty;

    /// <summary>Unit price (sector factor + console mod already applied for catalog).</summary>
    public int UnitPrice;

    /// <summary>False when the server cannot safely quote this purchase.</summary>
    public bool Available = true;

    public float Trend;
    public double ChangePercent;

    /// <summary>Null = unlimited catalog stock. Non-null = available resale units on this station.</summary>
    public int? StockQuantity;

    /// <summary>True when this row is from pallet-sold market stock, not YAML cargo catalog.</summary>
    public bool IsResale;

    public const string ResaleIdPrefix = "resale:";
    public const string ResaleCategoryKey = "cargoproduct-category-name-resale";

    public static string MakeResaleProductId(EntProtoId entityProtoId) => ResaleIdPrefix + entityProtoId.Id;

    public static bool TryParseResaleId(string productId, out EntProtoId entityProtoId)
    {
        entityProtoId = default;
        if (!productId.StartsWith(ResaleIdPrefix, StringComparison.Ordinal))
            return false;

        var id = productId[ResaleIdPrefix.Length..];
        if (id.Length == 0)
            return false;

        entityProtoId = id;
        return true;
    }
}
