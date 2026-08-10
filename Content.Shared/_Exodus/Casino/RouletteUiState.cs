using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Casino;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class RouletteVisualsComponent : Component
{
    [AutoNetworkedField]
    public RoulettePhase Phase;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan PhaseStartedAt;

    [AutoNetworkedField, AutoPausedField]
    public TimeSpan PhaseEndsAt;

    [AutoNetworkedField]
    public int WinningNumber = -1;

    [AutoNetworkedField]
    public uint RoundId;

    [AutoNetworkedField]
    public RouletteWorldBet[] WorldBets = [];
}

[Serializable, NetSerializable]
public enum RoulettePhase : byte
{
    Betting,
    Spinning,
    Payout
}

[Serializable, NetSerializable]
public enum RouletteUiKey : byte
{
    Key
}

[Serializable, NetSerializable]
public enum RouletteVisualLayers : byte
{
    Table,
    Wheel,
    Highlight,
    Ball
}

[Serializable, NetSerializable]
public sealed class RouletteUiState : BoundUserInterfaceState
{
    public RoulettePhase Phase { get; }
    public TimeSpan PhaseStartedAt { get; }
    public TimeSpan PhaseEndsAt { get; }
    public int WinningNumber { get; }
    public uint RoundId { get; }
    public int Balance { get; }
    public int TotalBet { get; }
    public int LastPayout { get; }
    public int MinimumBet { get; }
    public int MaximumBet { get; }
    public int MaximumBetsPerPlayer { get; }
    public TimeSpan BettingDuration { get; }
    public TimeSpan SpinDuration { get; }
    public TimeSpan PayoutDuration { get; }
    public RouletteBet[] Bets { get; }
    public RoulettePlayerBetSummary[] PlayerBets { get; }

    public RouletteUiState(
        RoulettePhase phase,
        TimeSpan phaseStartedAt,
        TimeSpan phaseEndsAt,
        int winningNumber,
        uint roundId,
        int balance,
        int totalBet,
        int lastPayout,
        int minimumBet,
        int maximumBet,
        int maximumBetsPerPlayer,
        TimeSpan bettingDuration,
        TimeSpan spinDuration,
        TimeSpan payoutDuration,
        RouletteBet[] bets,
        RoulettePlayerBetSummary[] playerBets)
    {
        Phase = phase;
        PhaseStartedAt = phaseStartedAt;
        PhaseEndsAt = phaseEndsAt;
        WinningNumber = winningNumber;
        RoundId = roundId;
        Balance = balance;
        TotalBet = totalBet;
        LastPayout = lastPayout;
        MinimumBet = minimumBet;
        MaximumBet = maximumBet;
        MaximumBetsPerPlayer = maximumBetsPerPlayer;
        BettingDuration = bettingDuration;
        SpinDuration = spinDuration;
        PayoutDuration = payoutDuration;
        Bets = bets;
        PlayerBets = playerBets;
    }
}
