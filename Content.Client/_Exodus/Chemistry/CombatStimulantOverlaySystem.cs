using Robust.Client.Graphics;

namespace Content.Client._Exodus.Chemistry;

public sealed partial class CombatStimulantOverlaySystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new CombatStimulantOverlay());
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<CombatStimulantOverlay>();
        base.Shutdown();
    }
}
