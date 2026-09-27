using Robust.Client.Graphics;

namespace Content.Client._Exodus.Genetics;

public sealed class GeneticVisionSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new GeneticNightVisionOverlay());
        _overlays.AddOverlay(new GeneticHearingOverlay());
        _overlays.AddOverlay(new GeneticPhotophobiaOverlay());
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<GeneticNightVisionOverlay>();
        _overlays.RemoveOverlay<GeneticHearingOverlay>();
        _overlays.RemoveOverlay<GeneticPhotophobiaOverlay>();
        base.Shutdown();
    }
}
