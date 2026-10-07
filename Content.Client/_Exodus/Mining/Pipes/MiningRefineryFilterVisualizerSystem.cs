using Content.Shared._Exodus.Mining.Pipes;
using Robust.Client.GameObjects;

namespace Content.Client._Exodus.Mining.Pipes;

/// <summary>Shows each filter in its own configured socket, including depleted cartridges.</summary>
public sealed partial class MiningRefineryFilterVisualizerSystem : EntitySystem
{
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SpriteSystem _sprites = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MiningRefineryComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(Entity<MiningRefineryComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || !_appearance.TryGetData<MiningRefineryFilterAppearance>(ent,
                MiningRefineryVisuals.Filters, out var appearance, args.Component))
            return;

        foreach (var slot in ent.Comp.FilterSlots)
        {
            if (!_sprites.LayerMapTryGet((ent, args.Sprite), slot, out var layer, false))
                continue;

            var state = appearance.States.GetValueOrDefault(slot);
            _sprites.LayerSetVisible((ent, args.Sprite), layer, state != MiningRefineryFilterState.Empty);
            _sprites.LayerSetRsiState((ent, args.Sprite), layer, state == MiningRefineryFilterState.Depleted
                ? ent.Comp.FilterDepletedState
                : ent.Comp.FilterIntactState);
        }
    }
}
