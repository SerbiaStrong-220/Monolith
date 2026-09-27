using Content.Client.Overlays;
using Content.Shared._Exodus.Genetics;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;

namespace Content.Client._Exodus.Genetics;

/// <summary>Biological ambient vision. Other night-vision sources retain their own overlay and settings.</summary>
public sealed class GeneticNightVisionOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IOverlayManager _overlays = default!;

    public override OverlaySpace Space => OverlaySpace.BeforeLighting;

    public GeneticNightVisionOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_overlays.HasOverlay<NightVisionOverlay>() || _player.LocalEntity is not { } player ||
            !_entities.TryGetComponent<GeneticEffectsComponent>(player, out var effects) ||
            effects.Reverting || !effects.NightVisionEnabled ||
            (effects.Modifiers.Abilities & GeneticAbility.NightVision) == 0 ||
            !_entities.TryGetComponent<MobStateComponent>(player, out var mob) || mob.CurrentState != MobState.Alive)
            return;

        args.WorldHandle.DrawRect(args.WorldBounds, effects.NightVisionColor);
    }
}
