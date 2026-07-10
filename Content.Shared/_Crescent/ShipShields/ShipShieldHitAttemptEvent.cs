using Robust.Shared.Map;

namespace Content.Shared._Crescent.ShipShields;

/// <summary>
/// Raised on a grid when something attempts to deliver an electrical/energetic strike to a
/// world-space point on it. The shield system listens for this and, if its bubble covers the
/// point, applies the load and sets <see cref="Absorbed"/>.
/// </summary>
/// <param name="Point">World-space impact point on the target grid's map.</param>
/// <param name="LoadWatts">Power load to add to the emitter if the shield absorbs the strike.</param>
/// <param name="Absorbed">Set to true by the shield system when the strike is absorbed.</param>
[ByRefEvent]
public record struct ShipShieldHitAttemptEvent(MapCoordinates Point, float LoadWatts, bool Absorbed);
