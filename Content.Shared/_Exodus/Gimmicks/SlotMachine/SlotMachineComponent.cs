using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Exodus.Gimmicks.SlotMachine;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class SlotMachineComponent : Component
{
    public const int MinBet = 1000;
    public const int MinInsert = 1000;
    public const string CreditStackId = "Credit";
    public const string CashPrototypeId = "SpaceCash";

    [DataField, AutoNetworkedField]
    public List<string> Reels = new() { "seven", "seven", "seven" };

    [DataField]
    public List<SlotMachineReelDef> ReelPools = new();

    [DataField]
    public List<SlotMachineRule> Rules = new();

    [DataField, AutoNetworkedField]
    public int StoredCredits;

    [DataField, AutoNetworkedField]
    public int LastBet;

    [DataField, AutoNetworkedField]
    public int LastPayout;

    [AutoNetworkedField]
    public bool IsWin;

    [AutoNetworkedField]
    public string WinText = string.Empty;

    [DataField]
    public SoundSpecifier InsertSound = new SoundPathSpecifier("/Audio/Machines/id_insert.ogg");

    [DataField]
    public SoundSpecifier SpinSound = new SoundPathSpecifier("/Audio/_Exodus/Gimmicks/SlotMachine/slot_spin.ogg");

    [DataField]
    public SoundSpecifier WinSound = new SoundPathSpecifier("/Audio/_Exodus/Gimmicks/SlotMachine/slot_win.ogg");

    [DataField]
    public TimeSpan CollectDuration = TimeSpan.FromSeconds(10);

    public bool HasPendingResult;
    public bool HasPendingCollection;

    [AutoPausedField]
    public TimeSpan CollectionEndTime;
    public List<string> PendingReels = new() { "seven", "seven", "seven" };
    public bool PendingIsWin;
    public string PendingWinText = string.Empty;
    public int PendingPayout;
    public EntityUid? PendingJackpotWinner;

    [AutoPausedField]
    public TimeSpan SpinEndTime;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class SlotMachineRule
{
    [DataField(required: true)]
    public List<string> Symbols = new();

    [DataField]
    public int? Index;

    [DataField(required: true)]
    public int Multiplier;

    [DataField(required: true)]
    public string WinText = string.Empty;

    [DataField]
    public Color MultiplierColor = Color.White;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class SlotMachineSymbolDef
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField]
    public string Name = "slot-machine-symbol-default";

    [DataField]
    public float Weight = 1f;

    [DataField]
    public SpriteSpecifier Icon = SpriteSpecifier.Invalid;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class SlotMachineReelDef
{
    [DataField(required: true)]
    public List<SlotMachineSymbolDef> Symbols = new();
}
