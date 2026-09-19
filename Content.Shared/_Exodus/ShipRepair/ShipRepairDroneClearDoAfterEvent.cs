using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.ShipRepair;

[Serializable, NetSerializable]
public sealed partial class ShipRepairDroneClearDoAfterEvent : SimpleDoAfterEvent;
