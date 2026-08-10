using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Casino;

[Serializable, NetSerializable]
public sealed class RoulettePlaceBetMessage : BoundUserInterfaceMessage
{
    public RouletteBet Bet { get; }
    public uint RoundId { get; }
    public uint RequestId { get; }

    public RoulettePlaceBetMessage(RouletteBet bet, uint roundId, uint requestId)
    {
        Bet = bet;
        RoundId = roundId;
        RequestId = requestId;
    }
}

[Serializable, NetSerializable]
public sealed class RouletteBetResultMessage : BoundUserInterfaceMessage
{
    public RouletteBetError Error { get; }
    public uint RequestId { get; }

    public RouletteBetResultMessage(RouletteBetError error, uint requestId)
    {
        Error = error;
        RequestId = requestId;
    }
}

[Serializable, NetSerializable]
public sealed class RouletteStateMessage : BoundUserInterfaceMessage
{
    public RouletteUiState State { get; }

    public RouletteStateMessage(RouletteUiState state)
    {
        State = state;
    }
}

[Serializable, NetSerializable]
public enum RouletteBetError : byte
{
    None,
    BettingClosed,
    InvalidBet,
    InsufficientFunds,
    DuplicateRequest,
    AccountUnavailable,
    LimitExceeded,
    TooManyBets
}
