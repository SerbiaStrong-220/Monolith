using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.TerritoryIncome;

[Serializable, NetSerializable]
public enum TerritoryIncomeUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class TerritoryIncomeUiState(
    string accountName,
    string currencyName,
    int balance,
    int points,
    int currencyPerPoint,
    int maxWithdrawal,
    TimeSpan interval,
    TimeSpan nextPayout,
    bool active,
    List<TerritoryIncomeEntry> territories) : BoundUserInterfaceState
{
    public string AccountName { get; } = accountName;
    public string CurrencyName { get; } = currencyName;
    public int Balance { get; } = balance;
    public int Points { get; } = points;
    public int CurrencyPerPoint { get; } = currencyPerPoint;
    public int MaxWithdrawal { get; } = maxWithdrawal;
    public TimeSpan Interval { get; } = interval;
    public TimeSpan NextPayout { get; } = nextPayout;
    public bool Active { get; } = active;
    public List<TerritoryIncomeEntry> Territories { get; } = territories;
}

[Serializable, NetSerializable]
public readonly record struct TerritoryIncomeEntry(string Name, int Points);

[Serializable, NetSerializable]
public sealed class TerritoryIncomeWithdrawMessage(int amount) : BoundUserInterfaceMessage
{
    public int Amount { get; } = amount;
}
