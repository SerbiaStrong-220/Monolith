// (c) Space Exodus Team - EXDS-RL with CLA
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Economy;

/// <summary>
/// One independently traded commodity. Quantities are stack units, gas moles, or individual objects.
/// ResaleUnitPrice bounds extracting individual stack units without changing the actual whole-stack appraisal.
/// Tax applies only to this commodity. ResaleTaxMultiplier also covers merging into another stack variant.
/// </summary>
public readonly record struct MarketBasketLine(
    EntProtoId PrototypeId,
    string MarketKey,
    double UnitBasePrice,
    double Quantity,
    bool IgnoreMarketModifier = false,
    EntityUid? SourceEntity = null,
    double? ResaleUnitPrice = null,
    MarketItemTax Tax = default,
    double? ResaleTaxMultiplier = null);

/// <summary>
/// A deterministic use-package replaced by its payload lines. Entries are ordered children before parents;
/// FirstNestedPackage identifies the contiguous descendants used to compare intact and unpacked payouts.
/// </summary>
public readonly record struct MarketBasketPackage(
    EntProtoId Prototype,
    double Quantity,
    int Uses,
    int FirstLine,
    int LineCount,
    int FirstNestedPackage);

/// <summary>
/// Immutable nominal composition, independent of current market factors and console modifiers.
/// A conservative random-content estimate must not be committed as an actual delivery.
/// </summary>
public sealed class MarketBasket
{
    public IReadOnlyList<MarketBasketLine> Lines { get; }
    public IReadOnlyList<MarketBasketPackage> Packages { get; }
    public int SpawnedUnits { get; }
    public double NominalValue { get; }
    public bool Exact { get; }

    public MarketBasket(IReadOnlyList<MarketBasketLine> lines, int spawnedUnits, bool exact,
        IReadOnlyList<MarketBasketPackage>? packages = null)
    {
        var copy = new MarketBasketLine[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            copy[i] = lines[i];
            NominalValue += lines[i].UnitBasePrice * lines[i].Quantity;
        }

        Lines = Array.AsReadOnly(copy);
        if (packages == null || packages.Count == 0)
            Packages = Array.Empty<MarketBasketPackage>();
        else
        {
            var packageCopy = new MarketBasketPackage[packages.Count];
            for (var i = 0; i < packages.Count; i++)
                packageCopy[i] = packages[i];
            Packages = Array.AsReadOnly(packageCopy);
        }
        SpawnedUnits = spawnedUnits;
        Exact = exact;
    }
}

/// <summary>
/// Prototype price adapters for systems whose runtime PriceCalculationEvent needs extra state.
/// OwnPrice excludes separately listed containers, ammunition, materials and gas. An adapter may
/// clear Failure only when it supplies a valid prototype appraisal for that unsupported state.
/// Prototype reload invalidates cached results; adapters must not depend on changing world state.
/// </summary>
[ByRefEvent]
public record struct MarketBasketPriceEvent(EntityPrototype Prototype, double OwnPrice)
{
    public bool Exact = true;
    public string? Failure;
}
