using Content.Shared._Exodus.ShipRepair;
using Robust.Client.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.ShipRepair;

/// <summary>Shows the actual item's preview beneath the normal animated SRD construction effect.</summary>
public sealed class ShipRepairConstructionVisualsSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ITileDefinitionManager _tiles = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShipRepairConstructionVisualsComponent, AfterAutoHandleStateEvent>(OnState);
    }

    private void OnState(Entity<ShipRepairConstructionVisualsComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        const int layer = 0;
        if (!ent.Comp.PreviewInitialized)
        {
            _sprite.AddBlankLayer((ent, sprite), layer);
            ent.Comp.PreviewInitialized = true;
        }
        if (ent.Comp.TargetPrototype is { } prototype && _prototypes.TryIndex(prototype, out var definition))
            _sprite.LayerSetTexture((ent, sprite), layer, _sprite.Frame0(definition));
        else if (ent.Comp.TileType != null && _tiles["Plating"].Sprite is { } plating)
            _sprite.LayerSetTexture((ent, sprite), layer, plating);
        else
        {
            _sprite.LayerSetVisible((ent, sprite), layer, false);
            return;
        }

        _sprite.LayerSetVisible((ent, sprite), layer, true);
        _sprite.LayerSetColor((ent, sprite), layer, new Color(255, 128, 0, 128));
        // Follow the saved construction's rotation, including rotation of its parent ship.
        // The original RCD animation keeps its own screen-facing sprite strategy.
        _sprite.SetGranularLayersRendering((ent, sprite), true);
        _sprite.LayerSetRenderingStrategy((ent, sprite), layer, LayerRenderingStrategy.Default);
        sprite.LayerSetShader(layer, "unshaded");
    }
}
