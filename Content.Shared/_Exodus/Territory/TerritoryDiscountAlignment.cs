namespace Content.Shared._Exodus.Territory;

/// <summary>
/// Defines how a territory faction's score affects store discounts.
/// Negative factions penalize every other faction individually, including other negative factions.
/// </summary>
public enum TerritoryDiscountAlignment
{
    Positive,
    Negative,
}
