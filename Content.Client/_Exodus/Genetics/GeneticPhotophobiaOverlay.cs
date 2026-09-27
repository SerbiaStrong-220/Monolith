using Content.Shared._Exodus.Genetics;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Genetics;

/// <summary>Wash out already visible, brightly lit areas without adding light or affecting camera windows.</summary>
public sealed class GeneticPhotophobiaOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ILightManager _lights = default!;

    private static readonly ProtoId<ShaderPrototype> Shader = "GeneticPhotophobia";
    private readonly ShaderInstance _shader;
    private readonly EntityQuery<GeneticEffectsComponent> _effects;
    private readonly EntityQuery<MobStateComponent> _mobs;
    private readonly EntityQuery<EyeComponent> _eyes;
    private readonly EntityQuery<BlindableComponent> _blindness;
    private readonly EntityQuery<MapComponent> _maps;
    private float _strength;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public GeneticPhotophobiaOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index(Shader).InstanceUnique();
        _effects = _entities.GetEntityQuery<GeneticEffectsComponent>();
        _mobs = _entities.GetEntityQuery<MobStateComponent>();
        _eyes = _entities.GetEntityQuery<EyeComponent>();
        _blindness = _entities.GetEntityQuery<BlindableComponent>();
        _maps = _entities.GetEntityQuery<MapComponent>();
        // Render before blindness and flash overlays, so their restrictions remain on top.
        ZIndex = -10;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_player.LocalEntity is not { } player ||
            !_effects.TryComp(player, out var effects) || effects.Reverting ||
            !float.IsFinite(effects.Modifiers.PhotophobiaStrength) || effects.Modifiers.PhotophobiaStrength <= 0 ||
            !_mobs.TryComp(player, out var mob) || mob.CurrentState != MobState.Alive ||
            !_eyes.TryComp(player, out var eye) || args.Viewport.Eye != eye.Eye ||
            (_blindness.TryComp(player, out var blind) && blind.IsBlind))
            return false;

        // The light buffer can contain an old frame when rendering with lighting disabled.
        if (!_lights.Enabled || !_lights.DrawLighting || args.Viewport.Eye is not { DrawLight: true } ||
            !_maps.TryComp(args.MapUid, out var map) || !map.LightingEnabled)
            return false;

        // Returning early above also avoids copying the viewport for unaffected players.
        _strength = Math.Clamp(effects.Modifiers.PhotophobiaStrength, 0f, 1f);
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("LIGHT_TEXTURE", args.Viewport.LightRenderTarget.Texture);
        _shader.SetParameter("Strength", _strength);
        var handle = args.WorldHandle;
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }

    protected override void DisposeBehavior()
    {
        _shader.Dispose();
        ScreenTexture = null;
        base.DisposeBehavior();
    }
}
