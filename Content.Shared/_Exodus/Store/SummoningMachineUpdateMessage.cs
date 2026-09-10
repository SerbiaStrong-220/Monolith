using Content.Shared.Store;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Store;

/// <summary>
/// Updates summoning timers for open interfaces without resending the store catalog.
/// The initial values are also included in StoreUpdateState when opening the interface.
/// </summary>
[Serializable, NetSerializable]
public sealed class SummoningMachineUpdateMessage(
    TimeSpan storedTime,
    StoreSummoningUiData? activeSummoning) : BoundUserInterfaceMessage
{
    public readonly TimeSpan StoredTime = storedTime;
    public readonly StoreSummoningUiData? ActiveSummoning = activeSummoning;
}
