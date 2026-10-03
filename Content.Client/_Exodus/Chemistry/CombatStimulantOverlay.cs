using Content.Shared._DV.CCVars;
using Content.Shared._Exodus.Chemistry;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Chemistry;

/// <summary>A local tint and gently pulsing vignette, restricted to the player's own viewport.</summary>
public sealed partial class CombatStimulantOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IConfigurationManager _configuration = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly ShaderInstance _shader;
    private readonly EntityQuery<CombatStimulantComponent> _effects;
    private readonly EntityQuery<MobStateComponent> _mobs;
    private readonly EntityQuery<EyeComponent> _eyes;
    private readonly EntityQuery<BlindableComponent> _blindness;
    private float _strength;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;
    public override bool RequestScreenTexture => true;

    public CombatStimulantOverlay()
    {
        IoCManager.InjectDependencies(this);
        _shader = _prototypes.Index<ShaderPrototype>("CombatStimulant").InstanceUnique();
        _effects = _entities.GetEntityQuery<CombatStimulantComponent>();
        _mobs = _entities.GetEntityQuery<MobStateComponent>();
        _eyes = _entities.GetEntityQuery<EyeComponent>();
        _blindness = _entities.GetEntityQuery<BlindableComponent>();
        ZIndex = -9;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_configuration.GetCVar(DCCVars.NoVisionFilters) || _player.LocalEntity is not { } player ||
            !_effects.TryComp(player, out var effect) || effect.ExpiresAt <= _timing.CurTime ||
            !_mobs.TryComp(player, out var mob) || mob.CurrentState != MobState.Alive ||
            !_eyes.TryComp(player, out var eye) || args.Viewport.Eye != eye.Eye ||
            (_blindness.TryComp(player, out var blind) && blind.IsBlind))
            return false;

        _strength = (float) Math.Clamp((effect.ExpiresAt - _timing.CurTime).TotalSeconds / 3, 0, 1);
        return true;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shader.SetParameter("Strength", _strength);
        _shader.SetParameter("Pulse", 0.75f + 0.25f * (float) Math.Sin(_timing.CurTime.TotalSeconds * 4));
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
