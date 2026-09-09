using Robust.Client.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Effects;

public sealed class WaveDistortionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();
        _overlays.AddOverlay(new WaveDistortionOverlay());
        SubscribeLocalEvent<WaveDistortionVisualsComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<WaveDistortionVisualsComponent, ComponentShutdown>(OnShutdown);
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WaveDistortionOverlay>();
        base.Shutdown();
    }

    private void OnStartup(Entity<WaveDistortionVisualsComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.Instance = _prototypes.Index(ent.Comp.Shader).InstanceUnique();
    }

    private void OnShutdown(Entity<WaveDistortionVisualsComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Instance?.Dispose();
        ent.Comp.Instance = null;
    }
}
