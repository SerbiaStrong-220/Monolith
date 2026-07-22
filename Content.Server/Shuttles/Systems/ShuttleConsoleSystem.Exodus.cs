using Content.Server._Exodus.Nebula;
using Robust.Shared.Map;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleConsoleSystem
{
    [Dependency] private NebulaSystem _nebula = default!;

    private bool CanFTLToNebula(EntityUid shuttleUid, EntityCoordinates targetCoordinates, Angle targetAngle, out string rejection)
    {
        return _nebula.CanFTL(shuttleUid, targetCoordinates, targetAngle, out rejection);
    }
}
