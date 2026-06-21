using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.NPC.Abilities;

/// <summary>Fake DoAfter, so players will see "building" progress bar.</summary>
[Serializable, NetSerializable]
public sealed partial class NpcCastDoAfterEvent : SimpleDoAfterEvent;
