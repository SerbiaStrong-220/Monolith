using Robust.Shared.Map;

namespace Content.Shared._Exodus.Examine;

/// <summary>
/// Raised on an examiner to use an alternative viewing origin or visibility mask.
/// A handled denial also prevents remote verb requests; target-side examine restrictions still apply.
/// </summary>
[ByRefEvent]
public record struct RemoteExamineEvent(MapCoordinates Target, EntityUid? Examined)
{
    public bool Handled;
    public bool Allowed;
}
