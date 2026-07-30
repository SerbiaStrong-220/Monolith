using Content.Shared._Exodus.Casino;
using Robust.Shared.Audio;
using Robust.Shared.Network;

namespace Content.Server._Exodus.Casino;

[RegisterComponent, Access(typeof(RouletteSystem))]
public sealed partial class RouletteComponent : Component
{
    [DataField]
    public TimeSpan BettingDuration = TimeSpan.FromSeconds(15);

    [DataField]
    public TimeSpan SpinDuration = TimeSpan.FromSeconds(8);

    [DataField]
    public TimeSpan PayoutDuration = TimeSpan.FromSeconds(4);

    [DataField]
    public int MinimumBet = 1;

    [DataField]
    public int MaximumBet = 100000;

    [DataField]
    public int MaximumBetsPerPlayer = 64;

    [DataField]
    public SoundSpecifier SpinSound = new SoundPathSpecifier("/Audio/_Exodus/Casino/roulette_ball_bounce.ogg");

    [DataField]
    public SoundSpecifier BetSound = new SoundPathSpecifier("/Audio/_Exodus/Casino/roulette_chip_place.ogg");

    public EntityUid? SpinAudioStream;

    public readonly List<RoulettePlayerBet> Bets = new();
    public readonly Dictionary<NetUserId, uint> LastRequestIds = new();
    public readonly Dictionary<NetUserId, int> LastPayouts = new();
    public readonly Dictionary<NetUserId, byte> PlayerSlots = new();
    public readonly Dictionary<NetUserId, RoulettePlayerCache> PlayerBets = new();
    public readonly Dictionary<(NetUserId Player, RouletteBetType Type, int Number), int> WorldBetIndices = new();
    public RouletteWorldBet[] WorldBets = [];
    public RoulettePlayerBetSummary[] PlayerBetSummaries = [];
    public bool Settled;
}

public readonly record struct RoulettePlayerBet(NetUserId Player, RouletteBet Bet, TimeSpan PlacedAt);

public sealed class RoulettePlayerCache
{
    public RouletteBet[] Bets = [];
    public int Total;
    public int SummaryIndex;
}
