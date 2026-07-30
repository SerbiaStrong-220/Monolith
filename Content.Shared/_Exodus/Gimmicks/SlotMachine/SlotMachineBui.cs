using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Gimmicks.SlotMachine;

[Serializable, NetSerializable]
public enum SlotMachineUiKey
{
    Key
}

[Serializable, NetSerializable]
public sealed class SlotMachineBoundUserInterfaceState(
    List<string> reels,
    int storedCredits,
    bool isWin,
    string winText,
    int lastBet,
    int lastPayout,
    bool isSpinning,
    bool isCollecting,
    List<SlotMachineRule> rules,
    List<SlotMachineReelDef> reelPools) : BoundUserInterfaceState
{
    public List<string> Reels { get; } = reels;
    public int StoredCredits { get; } = storedCredits;
    public bool IsWin { get; } = isWin;
    public string WinText { get; } = winText;
    public int LastBet { get; } = lastBet;
    public int LastPayout { get; } = lastPayout;
    public bool IsSpinning { get; } = isSpinning;
    public bool IsCollecting { get; } = isCollecting;
    public List<SlotMachineRule> Rules { get; } = rules;
    public List<SlotMachineReelDef> ReelPools { get; } = reelPools;
}

[Serializable, NetSerializable]
public sealed class SlotMachineSpinMessage(int bet) : BoundUserInterfaceMessage
{
    public int Bet { get; } = bet;
}

[Serializable, NetSerializable]
public sealed class SlotMachineInsertMessage(int amount) : BoundUserInterfaceMessage
{
    public int Amount { get; } = amount;
}

[Serializable, NetSerializable]
public sealed class SlotMachineCollectMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed partial class SlotMachineCollectDoAfterEvent : SimpleDoAfterEvent
{
}
