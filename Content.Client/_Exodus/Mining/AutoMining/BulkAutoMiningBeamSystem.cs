using Robust.Client.Graphics;

namespace Content.Client._Exodus.Mining.AutoMining;

public sealed partial class BulkAutoMiningBeamSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlay = default!;

    private BulkAutoMiningBeamOverlay? _beamOverlay;

    public override void Initialize()
    {
        base.Initialize();
        _beamOverlay = new BulkAutoMiningBeamOverlay();
        _overlay.AddOverlay(_beamOverlay);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        if (_beamOverlay != null)
            _overlay.RemoveOverlay(_beamOverlay);
    }
}
