using Content.Shared.Exodus.SpaceArtillery;
using Robust.Server.GameStates;

namespace Content.Server.Exodus.SpaceArtillery;

public sealed class ShipGrapplingGunSystem : SharedShipGrapplingGunSystem
{
    [Dependency] private readonly PvsOverrideSystem _override = default!;

    protected override void PvsOverride(EntityUid uid)
    {
        base.PvsOverride(uid);

        _override.AddGlobalOverride(uid);
    }
}
