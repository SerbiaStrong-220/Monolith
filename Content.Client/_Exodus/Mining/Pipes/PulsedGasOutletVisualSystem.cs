using Content.Shared._Exodus.Mining.Pipes;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._Exodus.Mining.Pipes;

/// <summary>Plays baked particle frames locally; the server only synchronizes actual discharge timestamps.</summary>
public sealed class PulsedGasOutletVisualSystem : EntitySystem
{
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        var query = EntityQueryEnumerator<PulsedGasOutletComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var outlet, out var sprite))
        {
            var age = outlet.LastPulse is { } pulse ? _timing.CurTime - pulse : TimeSpan.MaxValue;
            var active = age >= TimeSpan.Zero && age < outlet.EffectDuration;
            Entity<SpriteComponent?> ent = (uid, sprite);
            _sprites.LayerSetRsiState(ent, PulsedGasOutletVisualLayers.Body, active ? "venting" : "base");
            _sprites.LayerSetColor(ent, PulsedGasOutletVisualLayers.Indicator,
                active ? Color.Orange : outlet.Enabled ? Color.LimeGreen : Color.DarkRed);
            if (!_sprites.TryGetLayer(ent, PulsedGasOutletVisualLayers.Plume, out var plume, true))
                continue;

            _sprites.LayerSetVisible(plume, active);
            _sprites.LayerSetAutoAnimated(plume, false);
            if (active && plume.ActualState is { } state)
            {
                var progress = (float)(age.TotalSeconds / outlet.EffectDuration.TotalSeconds);
                _sprites.LayerSetAnimationTime(plume, progress * state.AnimationLength);
            }
        }
    }
}
