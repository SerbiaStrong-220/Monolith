using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Events;

/// <summary>
/// Raised on an entity when its nebula membership changes — including when it enters a nebula
/// (<see cref="OldMarker"/> default), leaves one (<see cref="NewMarker"/> default), or moves
/// between two of different kinds.
/// </summary>
[ByRefEvent]
public readonly record struct NebulaPresenceChangedEvent(EntityUid Entity, EntProtoId OldMarker, EntProtoId NewMarker);
