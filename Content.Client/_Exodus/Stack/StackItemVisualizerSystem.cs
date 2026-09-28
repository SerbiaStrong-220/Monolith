using Content.Shared._Exodus.Stack;
using Content.Shared.Stacks;
using Robust.Client.GameObjects;

namespace Content.Client._Exodus.Stack;

public sealed partial class StackItemVisualizerSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprites = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<StackItemComponent, AppearanceChangeEvent>(OnAppearanceChange);
        SubscribeLocalEvent<StackItemComponent, AfterAutoHandleStateEvent>(OnAfterAutoHandleState);
    }

    private void OnAfterAutoHandleState(Entity<StackItemComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (TryComp<AppearanceComponent>(ent, out var appearance))
            _appearance.QueueUpdate(ent, appearance);
    }

    private void OnAppearanceChange(Entity<StackItemComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || !_appearance.TryGetData<int>(ent, StackVisuals.Actual, out var count, args.Component))
            return;

        foreach (var (layer, threshold) in ent.Comp.LayerThresholds)
            _sprites.LayerSetVisible((ent.Owner, args.Sprite), layer, count >= threshold);
    }
}
