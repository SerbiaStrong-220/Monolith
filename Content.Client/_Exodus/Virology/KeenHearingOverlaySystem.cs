using Robust.Client.Graphics;

namespace Content.Client._Exodus.Virology;

public sealed class KeenHearingOverlaySystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        _overlays.AddOverlay(new KeenHearingOverlay());
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<KeenHearingOverlay>();
    }
}
