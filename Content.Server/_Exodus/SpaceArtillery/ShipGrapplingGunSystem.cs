using Content.Server.Shuttles.Events;
using Content.Shared.Exodus.SpaceArtillery;
using Content.Shared.Exodus.SpaceArtillery.Components;
using Robust.Server.GameStates;

namespace Content.Server.Exodus.SpaceArtillery;

public sealed class ShipGrapplingGunSystem : SharedShipGrapplingGunSystem
{
    [Dependency] private readonly PvsOverrideSystem _override = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ShipGrapplingTargetGridComponent, FTLStartedEvent>(OnFTLStart);
    }

    private void OnFTLStart(EntityUid uid, ShipGrapplingTargetGridComponent component, ref FTLStartedEvent args)
    {
        if (!TryComp<ShipGrapplingGunComponent>(component.Gun, out var grapComp))
            return;

        Ungrapple((component.Gun, grapComp), true);
    }

    protected override void PvsOverride(EntityUid uid)
    {
        base.PvsOverride(uid);

        _override.AddGlobalOverride(uid);
    }

    protected override void RemovePvsOverride(EntityUid uid)
    {
        base.RemovePvsOverride(uid);

        _override.RemoveGlobalOverride(uid);
    }
}
