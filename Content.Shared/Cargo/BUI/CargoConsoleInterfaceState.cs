using Content.Shared._Exodus.Economy; // Exodus dynamic market
using Robust.Shared.Serialization;

namespace Content.Shared.Cargo.BUI;

[NetSerializable, Serializable]
public sealed class CargoConsoleInterfaceState : BoundUserInterfaceState
{
    public string Name;
    public int Count;
    public int Capacity;
    public int Balance;
    public List<CargoOrderData> Orders;

    /// <summary>
    /// Exodus: live catalog prices + trends from the global sector market.
    /// Null falls back to static prototype costs on the client; an empty list is an empty catalog.
    /// </summary>
    public List<CargoMarketListing>? MarketListings; // Exodus dynamic market

    public CargoConsoleInterfaceState(
        string name,
        int count,
        int capacity,
        int balance,
        List<CargoOrderData> orders,
        List<CargoMarketListing>? marketListings = null) // Exodus marketListings
    {
        Name = name;
        Count = count;
        Capacity = capacity;
        Balance = balance;
        Orders = orders;
        MarketListings = marketListings; // Exodus
    }
}
