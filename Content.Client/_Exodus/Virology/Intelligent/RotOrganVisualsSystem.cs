using Content.Shared._Exodus.Virology.Intelligent;
using Robust.Client.Animations;
using Robust.Client.GameObjects;

namespace Content.Client._Exodus.Virology.Intelligent;

public sealed class RotOrganVisualsSystem : VisualizerSystem<RotOrganVisualsComponent>
{
    private const string GrowthAnimationKey = "rot-organ-growth";

    private enum Layers : byte
    {
        Growing,
    }

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RotOrganVisualsComponent, AnimationCompletedEvent>(OnAnimationCompleted);
    }

    protected override void OnAppearanceChange(EntityUid uid, RotOrganVisualsComponent comp, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || !comp.Enabled)
            return;
        var connected = AppearanceSystem.TryGetData<bool>(uid, RotOrganVisuals.Connected, out var active, args.Component) && active;
        var state = connected ? comp.ActiveState : comp.DormantState;
        if (comp.Nutrition && AppearanceSystem.TryGetData<bool>(uid, RotOrganVisuals.Nutrition, out var consumed, args.Component))
            state = consumed ? comp.PartialState : comp.FullState;
        if (comp.Tissue && AppearanceSystem.TryGetData<int>(uid, RotOrganVisuals.Connections, out var connections, args.Component))
            state = connections.ToString();
        else if (connected && comp.GrowthPrefix != null
            && AppearanceSystem.TryGetData<int>(uid, RotOrganVisuals.Growth, out var stage, args.Component))
            state = comp.GrowthPrefix + stage;
        if (!TryComp<RotOrganAnimationComponent>(uid, out var animation))
        {
            animation = AddComp<RotOrganAnimationComponent>(uid);
            SpriteSystem.LayerMapSet((uid, args.Sprite), Layers.Growing, comp.Layer);
            AnimationSystem.Play(uid, new Animation
            {
                Length = comp.GrowDuration,
                AnimationTracks =
                {
                    new AnimationTrackSpriteFlick
                    {
                        LayerKey = Layers.Growing,
                        KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(comp.GrowingState, 0f) },
                    },
                },
            }, GrowthAnimationKey);
        }
        animation.DesiredState = state;
        if (animation.Complete)
            SpriteSystem.LayerSetRsiState((uid, args.Sprite), comp.Layer, state);
        if (comp.Tissue)
            SpriteSystem.SetColor((uid, args.Sprite), connected ? Color.White : Color.Gray);
    }

    private void OnAnimationCompleted(Entity<RotOrganVisualsComponent> ent, ref AnimationCompletedEvent args)
    {
        if (args.Key != GrowthAnimationKey || !TryComp<RotOrganAnimationComponent>(ent, out var animation)
            || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        animation.Complete = true;
        SpriteSystem.LayerSetRsiState((ent, sprite), ent.Comp.Layer, animation.DesiredState);
        SpriteSystem.LayerSetAutoAnimated((ent, sprite), ent.Comp.Layer, true);
        SpriteSystem.LayerSetAnimationTime((ent, sprite), ent.Comp.Layer, 0);
    }
}
