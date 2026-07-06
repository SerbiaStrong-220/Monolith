using System.Numerics;
using Content.Client.Actions;
using Content.Client.Decals.Overlays;
using Content.Client._Exodus.Decals; // Exodus
using Content.Shared.Actions;
using Content.Shared.Decals;
using Content.Shared.Input; // Exodus
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map; // Exodus
using Robust.Shared.Player; // Exodus
using Robust.Shared.Prototypes;

namespace Content.Client.Decals;

// This is shit and basically a half-rewrite of PlacementManager
// TODO refactor placementmanager so this isnt shit anymore
public sealed partial class DecalPlacementSystem : EntitySystem
{
    [Dependency] private IInputManager _inputManager = default!;
    [Dependency] private IMapManager _mapManager = default!; // Exodus
    [Dependency] private IOverlayManager _overlay = default!;
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private InputSystem _inputSystem = default!;
    [Dependency] private MetaDataSystem _metaData = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private IDecalPlacementManager _tool = default!; // Exodus

    private string? _decalId;
    private Color _decalColor = Color.White;
    private Angle _decalAngle = Angle.Zero;
    private bool _snap;
    private int _zIndex;
    private bool _cleanable;

    private bool _active;
    private bool _placing;
    private bool _erasing;

    public (DecalPrototype? Decal, bool Snap, Angle Angle, Color Color) GetActiveDecal()
    {
        return _active && _decalId != null ?
            (_protoMan.Index<DecalPrototype>(_decalId), _snap, _decalAngle, _decalColor) :
            (null, false, Angle.Zero, Color.Wheat);
    }

    public override void Initialize()
    {
        base.Initialize();
        _overlay.AddOverlay(new DecalPlacementOverlay(this, _transform, _sprite));

        CommandBinds.Builder.Bind(EngineKeyFunctions.EditorPlaceObject, new PointerStateInputCmdHandler(
            (session, coords, uid) =>
            {
                // Exodus-Start
                if (_tool.EyedropperActive)
                {
                    _tool.SetEyedropper(false);

                    if (TryPickDecalColor(coords, out var picked))
                        _tool.NotifyEyedropperColorPicked(picked);

                    return true;
                }
                if (_tool.Stamping)
                {
                    PlaceStamp(coords);
                    return true;
                }
                // Exodus-End

                if (!_active || _placing || _decalId == null)
                    return false;

                _placing = true;

                if (_snap)
                {
                    var newPos = new Vector2(
                        (float) (MathF.Round(coords.X - 0.5f, MidpointRounding.AwayFromZero) + 0.5),
                        (float) (MathF.Round(coords.Y - 0.5f, MidpointRounding.AwayFromZero) + 0.5)
                    );
                    coords = coords.WithPosition(newPos);
                }

                coords = coords.Offset(new Vector2(-0.5f, -0.5f));

                if (!coords.IsValid(EntityManager))
                    return false;

                var decal = new Decal(coords.Position, _decalId, _decalColor, _decalAngle, _zIndex, _cleanable);
                RaiseNetworkEvent(new RequestDecalPlacementEvent(decal, GetNetCoordinates(coords)));

                return true;
            },
            (session, coords, uid) =>
            {
                if (!_active)
                    return false;

                _placing = false;
                return true;
            }, true))
            .Bind(EngineKeyFunctions.EditorCancelPlace, new PointerStateInputCmdHandler(
            (session, coords, uid) =>
            {
                // Exodus-Start
                if (_tool.EyedropperActive)
                {
                    _tool.SetEyedropper(false);
                    return true;
                }

                if (_tool.Stamping)
                {
                    _tool.ClearStamp();
                    return true;
                }
                // Exodus-End

                if (!_active || _erasing)
                    return false;

                _erasing = true;

                RaiseNetworkEvent(new RequestDecalRemovalEvent(GetNetCoordinates(coords)));

                return true;
            }, (session, coords, uid) =>
            {
                if (!_active)
                    return false;
                _erasing = false;

                return true;
            }, true))
            // Exodus-Start copy the decal (O - not 0) or the whole stack (Ctrl+O) under the cursor.
            .Bind(ContentKeyFunctions.EditorCopyDecal, new PointerInputCmdHandler(OnCopyDecal))
            .Bind(ContentKeyFunctions.EditorCopyDecalStack, new PointerInputCmdHandler(OnCopyDecalStack))
            // Exodus-End
            .Register<DecalPlacementSystem>();

        SubscribeLocalEvent<FillActionSlotEvent>(OnFillSlot);
        SubscribeLocalEvent<PlaceDecalActionEvent>(OnPlaceDecalAction);
    }

    private void OnPlaceDecalAction(PlaceDecalActionEvent args)
    {
        if (args.Handled)
            return;

        if (args.Target.GetGridUid(EntityManager) == null)
            return;

        args.Handled = true;

        if (args.Snap)
        {
            var newPos = new Vector2(
                (float) (MathF.Round(args.Target.X - 0.5f, MidpointRounding.AwayFromZero) + 0.5),
                (float) (MathF.Round(args.Target.Y - 0.5f, MidpointRounding.AwayFromZero) + 0.5)
            );
            args.Target = args.Target.WithPosition(newPos);
        }

        args.Target = args.Target.Offset(new Vector2(-0.5f, -0.5f));

        var decal = new Decal(args.Target.Position, args.DecalId, args.Color, Angle.FromDegrees(args.Rotation), args.ZIndex, args.Cleanable);
        RaiseNetworkEvent(new RequestDecalPlacementEvent(decal, GetNetCoordinates(args.Target)));
    }

    private void OnFillSlot(FillActionSlotEvent ev)
    {
        if (!_active || _placing)
            return;

        if (ev.Action != null)
            return;

        if (_decalId == null || !_protoMan.TryIndex<DecalPrototype>(_decalId, out var decalProto))
            return;

        var actionEvent = new PlaceDecalActionEvent()
        {
            DecalId = _decalId,
            Color = _decalColor,
            Rotation = _decalAngle.Degrees,
            Snap = _snap,
            ZIndex = _zIndex,
            Cleanable = _cleanable,
        };

        var actionId = Spawn(null);
        AddComp(actionId, new WorldTargetActionComponent
        {
            // non-unique actions may be considered duplicates when saving/loading.
            Icon = decalProto.Sprite,
            Repeat = true,
            ClientExclusive = true,
            CheckCanAccess = false,
            CheckCanInteract = false,
            Range = -1,
            Event = actionEvent,
            IconColor = _decalColor,
        });

        _metaData.SetEntityName(actionId, $"{_decalId} ({_decalColor.ToHex()}, {(int) _decalAngle.Degrees})");

        ev.Action = actionId;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _overlay.RemoveOverlay<DecalPlacementOverlay>();
        CommandBinds.Unregister<DecalPlacementSystem>();
    }

    public void UpdateDecalInfo(string id, Color color, float rotation, bool snap, int zIndex, bool cleanable)
    {
        _decalId = id;
        _decalColor = color;
        _decalAngle = Angle.FromDegrees(rotation);
        _snap = snap;
        _zIndex = zIndex;
        _cleanable = cleanable;
    }

    // Exodus-Start: clear the active decal. This wast here for years, really?!
    public void ClearDecal()
    {
        _decalId = null;
    }
    // Exodus-End

    public void SetActive(bool active)
    {
        _active = active;
        _tool.SetActive(active); // Exodus mirror active state so the tool manager can reset/gate itself
        if (_active)
            _inputManager.Contexts.SetActiveContext("editor");
        else
            _inputSystem.SetEntityContextActive();
    }

    // Exodus-Start
    private bool OnCopyDecal(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!_active)
            return false;

        _tool.SetEyedropper(false);
        _tool.ClearStamp();

        if (TryGetDecalsUnder(coords, out _, out var decals) && Topmost(decals) is { } top)
            _tool.NotifyDecalCopied(top.Decal);

        return true;
    }

    private bool OnCopyDecalStack(ICommonSession? session, EntityCoordinates coords, EntityUid uid)
    {
        if (!_active)
            return false;

        _tool.SetEyedropper(false);
        _tool.ClearStamp();

        if (!TryGetDecalsUnder(coords, out var localPos, out var decals))
            return true;

        // So stamp stays tile-aligned when placed.
        var origin = localPos.Floored();
        var stamp = new List<(Vector2 Offset, Decal Decal)>();
        foreach (var (_, decal) in decals)
            stamp.Add((decal.Coordinates - origin, decal));

        _tool.BeginStamp(stamp);
        return true;
    }

    private void PlaceStamp(EntityCoordinates coords)
    {
        if (!TryGetGridLocal(coords, out var gridUid, out var localPos))
            return;

        var origin = localPos.Floored();
        foreach (var (offset, decal) in _tool.Stamp)
        {
            var newPos = origin + offset;
            var target = new EntityCoordinates(gridUid, newPos);
            if (!target.IsValid(EntityManager))
                continue;

            var copy = new Decal(newPos, decal.Id, decal.Color, decal.Angle, decal.ZIndex, decal.Cleanable);
            RaiseNetworkEvent(new RequestDecalPlacementEvent(copy, GetNetCoordinates(target)));
        }
    }

    private bool TryPickDecalColor(EntityCoordinates coords, out Color color)
    {
        color = Color.White;
        if (!TryGetDecalsUnder(coords, out _, out var decals) || Topmost(decals) is not { } top)
            return false;

        color = top.Decal.Color ?? Color.White;
        return true;
    }

    /// <summary>Resolve the grid and grid-local position under the given coordinates.</summary>
    private bool TryGetGridLocal(EntityCoordinates coords, out EntityUid gridUid, out Vector2 localPos)
    {
        localPos = default;
        var mapPos = _transform.ToMapCoordinates(coords);
        if (!_mapManager.TryFindGridAt(mapPos, out gridUid, out _))
            return false;

        localPos = Vector2.Transform(mapPos.Position, _transform.GetInvWorldMatrix(gridUid));
        return true;
    }

    /// <summary>Collect every decal whose 1x1 footprint contains the cursor.</summary>
    private bool TryGetDecalsUnder(EntityCoordinates coords, out Vector2 localPos, out List<(uint Index, Decal Decal)> decals)
    {
        decals = new();
        if (!TryGetGridLocal(coords, out var gridUid, out localPos)
            || !TryComp<DecalGridComponent>(gridUid, out var decalGrid))
            return false;

        var chunkIndices = SharedDecalSystem.GetChunkIndices(localPos);
        if (!decalGrid.ChunkCollection.ChunkCollection.TryGetValue(chunkIndices, out var chunk))
            return false;

        foreach (var (id, decal) in chunk.Decals)
        {
            // Decals are drawn as a 1x1 tile with their bottom-left corner at Coordinates.
            if (localPos.X < decal.Coordinates.X || localPos.X >= decal.Coordinates.X + 1f ||
                localPos.Y < decal.Coordinates.Y || localPos.Y >= decal.Coordinates.Y + 1f)
                continue;

            decals.Add((id, decal));
        }

        return decals.Count > 0;
    }

    /// <summary>Pick the decal drawn on top: highest ZIndex, then highest id (matches the overlay).</summary>
    private static (uint Index, Decal Decal)? Topmost(List<(uint Index, Decal Decal)> decals)
    {
        (uint Index, Decal Decal)? best = null;
        foreach (var entry in decals)
        {
            if (best is not { } b
                || entry.Decal.ZIndex > b.Decal.ZIndex
                || (entry.Decal.ZIndex == b.Decal.ZIndex && entry.Index > b.Index))
                best = entry;
        }

        return best;
    }
    // Exodus-End
}
