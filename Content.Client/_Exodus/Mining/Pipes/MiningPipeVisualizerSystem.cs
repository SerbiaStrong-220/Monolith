using Content.Client.SubFloor;
using Content.Shared._Exodus.Mining.Pipes;
using Content.Shared.Wires;
using Robust.Client.GameObjects;

namespace Content.Client._Exodus.Mining.Pipes;

public sealed partial class MiningPipeVisualizerSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MiningPipeVisualizerComponent, AppearanceChangeEvent>(OnAppearanceChange,
            after: new[] { typeof(SubFloorHideSystem) });
    }

    private void OnAppearanceChange(Entity<MiningPipeVisualizerComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite is not { Visible: true } sprite)
            return;

        _appearance.TryGetData<WireVisDirFlags>(ent, WireVisVisuals.ConnectedMask, out var mask, args.Component);
        _appearance.TryGetData<bool>(ent, MiningPipeVisuals.Connected, out var connected, args.Component);
        var prefix = connected ? ent.Comp.FlowPrefix : ent.Comp.IdlePrefix;
        _sprite.LayerSetRsiState((ent, sprite), 0, $"{prefix}{(int)mask}");
    }
}
