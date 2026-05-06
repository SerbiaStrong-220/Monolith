using Content.Client._Exodus.Nebula;
using Robust.Shared.Map;

namespace Content.Client.Shuttles.UI;

public sealed partial class ShuttleMapControl
{
    private readonly NebulaSystem _nebula;

    private bool CanFTLToNebulaPreview(EntityUid shuttleUid, EntityCoordinates targetCoordinates, Angle targetAngle)
    {
        return _nebula.CanFTL(shuttleUid, targetCoordinates, targetAngle, out _);
    }
}
