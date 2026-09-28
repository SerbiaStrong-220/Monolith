using System.Numerics;
using Content.Client.UserInterface.Systems.Actions;
using Content.Client.Resources;
using Content.Shared.Maps;
using Content.Shared._Exodus.Virology.Intelligent;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Client._Exodus.Virology.Intelligent;

public sealed partial class RotBuildOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IInputManager _input = default!;
    [Dependency] private IEyeManager _eye = default!;
    [Dependency] private IMapManager _maps = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IResourceCache _resources = default!;
    private readonly HashSet<EntityUid> _occupants = [];
    private readonly Font _font;
    private string? _reason;
    public override OverlaySpace Space => OverlaySpace.WorldSpaceEntities | OverlaySpace.ScreenSpace;

    public RotBuildOverlay()
    {
        IoCManager.InjectDependencies(this);
        _font = _resources.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 11);
        ZIndex = 1000;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (args.Space == OverlaySpace.ScreenSpace)
        {
            if (_reason != null)
                args.ScreenHandle.DrawString(_font, _input.MouseScreenPosition.Position + new Vector2(18, 28), Loc.GetString(_reason));
            return;
        }
        _reason = null;
        if (_player.LocalEntity is not { } core || !_entities.TryGetComponent<RotIntelligentComponent>(core, out var colony)
            || !colony.Rooted || colony.ChangingForm || colony.BuildAction == null || _ui.GetUIController<ActionUIController>().SelectingTargetFor != colony.BuildAction
            || !_prototypes.TryIndex(colony.SelectedBuilding, out var recipe))
            return;
        var position = _eye.PixelToMap(_input.MouseScreenPosition);
        if (position.MapId != args.MapId || !_maps.TryFindGridAt(position, out var gridUid, out var grid))
            return;
        var transform = _entities.System<SharedTransformSystem>();
        var maps = _entities.System<SharedMapSystem>();
        var vision = _entities.System<RotIntelligentSystem>();
        var tile = maps.TileIndicesFor(gridUid, grid, transform.ToCoordinates(gridUid, position));
        var allowed = colony.Alive && colony.Biomass >= recipe.Cost
            && _entities.GetComponent<TransformComponent>(core).GridUid == gridUid;
        _reason = colony.Biomass < recipe.Cost ? "rot-intelligent-insufficient-biomass"
            : !allowed ? "rot-intelligent-no-grid" : null;
        var lookup = _entities.System<EntityLookupSystem>();
        var placement = _entities.System<RotPlacementSystem>();
        var turf = _entities.System<TurfSystem>();
        foreach (var cell in RotGeometry.Cells(tile, recipe.Size, colony.Rotation))
        {
            var point = maps.GridTileToLocal(gridUid, grid, cell);
            if (turf.IsSpace(maps.GetTileRef(gridUid, grid, cell)))
                _reason ??= "rot-intelligent-needs-floor";
            if (!vision.CanSee(core, transform.ToMapCoordinates(point)))
                _reason ??= "rot-intelligent-not-visible";
            var supported = HasSupport(cell);
            var converting = false;
            _occupants.Clear();
            lookup.GetLocalEntitiesIntersecting(gridUid, Box2.CenteredAround(point.Position, new Vector2(.9f)), _occupants,
                flags: LookupFlags.Uncontained);
            foreach (var uid in _occupants)
            {
                switch (placement.Classify((core, colony), recipe, uid))
                {
                    case RotOccupant.OtherColony:
                        _reason ??= "rot-intelligent-other-colony";
                        break;
                    case RotOccupant.Tissue:
                        if (recipe.Expansion)
                            _reason ??= "rot-intelligent-occupied";
                        break;
                    case RotOccupant.Blocked:
                        _reason ??= "rot-intelligent-occupied";
                        break;
                    case RotOccupant.ConvertibleWall:
                        converting = true;
                        break;
                }
            }
            if (colony.NetworkReady && !supported && (!(recipe.Expansion || converting) || !(HasSupport(cell + Vector2i.Up)
                    || HasSupport(cell + Vector2i.Down) || HasSupport(cell + Vector2i.Left) || HasSupport(cell + Vector2i.Right))))
                _reason ??= "rot-intelligent-needs-territory";
        }
        if (recipe.RequiresExhaust && !turf.IsSpace(maps.GetTileRef(gridUid, grid,
            tile + RotGeometry.Rotate(new Vector2i(0, recipe.Size.Y), colony.Rotation))))
            _reason ??= "rot-intelligent-exhaust-blocked";
        allowed &= _reason == null;
        _reason ??= "rot-intelligent-placement-ready";
        var handle = args.WorldHandle;
        handle.SetTransform(transform.GetWorldMatrix(gridUid));
        var fill = allowed ? new Color(0.3f, 0.8f, 0.45f, 0.22f) : new Color(0.95f, 0.25f, 0.2f, 0.25f);
        var outline = allowed ? Color.LightGreen : Color.OrangeRed;
        foreach (var cell in RotGeometry.Cells(tile, recipe.Size, colony.Rotation))
        {
            var box = new Box2(new Vector2(cell.X, cell.Y) * grid.TileSize,
                new Vector2(cell.X + 1, cell.Y + 1) * grid.TileSize);
            handle.DrawRect(box, fill);
            handle.DrawRect(box, outline, filled: false);
        }
        var center = (new Vector2(tile.X, tile.Y) + new Vector2(0.5f)) * grid.TileSize;
        var direction = RotGeometry.Rotate(new Vector2i(0, -1), colony.Rotation);
        var forward = new Vector2(direction.X, direction.Y);
        var end = center + forward * 0.4f;
        var side = new Vector2(-forward.Y, forward.X) * 0.15f;
        handle.DrawLine(center, end, outline);
        handle.DrawLine(end, end - forward * 0.2f + side, outline);
        handle.DrawLine(end, end - forward * 0.2f - side, outline);
        handle.SetTransform(Matrix3x2.Identity);

        bool HasSupport(Vector2i cell)
        {
            foreach (var uid in maps.GetAnchoredEntities(gridUid, grid, cell))
            {
                if (_entities.TryGetComponent<RotColonyMemberComponent>(uid, out var member)
                    && member.Core == core && member.Connected && member.Conductive)
                    return true;
            }
            return false;
        }
    }
}
