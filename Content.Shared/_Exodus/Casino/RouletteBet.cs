using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Casino;

[Serializable, NetSerializable]
public enum RouletteBetType : byte
{
    Number,
    Red,
    Black,
    Even,
    Odd,
    Low,
    High,
    FirstDozen,
    SecondDozen,
    ThirdDozen
}

[Serializable, NetSerializable]
public readonly record struct RouletteBet(RouletteBetType Type, int Number, int Amount);

[Serializable, NetSerializable]
public readonly record struct RoulettePlayerBetSummary(string PlayerName, int TotalBet);

[Serializable, NetSerializable]
public readonly record struct RouletteWorldBet(
    string PlayerName,
    RouletteBetType Type,
    int Number,
    int Amount,
    byte PlayerSlot,
    TimeSpan PlacedAt);
