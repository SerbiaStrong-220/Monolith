using System.Numerics;
using Content.Client.Actions;
using Content.Client.Decals.Overlays;
using Content.Shared.Actions;
using Content.Shared.Decals;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Map; // Exodus
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

    private string? _decalId;
    private Color _decalColor = Color.White;
    private Angle _decalAngle = Angle.Zero;
    private bool _snap;
    private int _zIndex;
    private bool _cleanable;

    private bool _active;
    private bool _placing;
    private bool _erasing;

    // Exodus-Start: eyedropper tool that copies the color of an existing decal on the map.
    private bool _eyedropper;

    /// <summary>
    ///     Whether the eyedropper (color picker) tool is currently selected.
    /// </summary>
    public bool EyedropperActive => _eyedropper;

    /// <summary>
    ///     Raised when the eyedropper successfully copies a color from a decal on the map.
    /// </summary>
    public event Action<Color>? EyedropperPicked;

    public void SetEyedropper(bool active)
    {
        _eyedropper = active && _active;
    }
    // Exodus-End

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
                // Exodus-Start: left click while the eyedropper is active copies a color instead of placing.
                if (_eyedropper)
                {
                    _eyedropper = false;

                    if (TryPickDecalColor(coords, out var picked))
                        EyedropperPicked?.Invoke(picked);

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
                // Exodus-Start: right click cancels the eyedropper instead of erasing.
                if (_eyedropper)
                {
                    _eyedropper = false;
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
            }, true)).Register<DecalPlacementSystem>();

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

    // Exodus: clear the active decal so it stops following the cursor when deselected.
    public void ClearDecal()
    {
        _decalId = null;
    }

    public void SetActive(bool active)
    {
        _active = active;
        _eyedropper = false; // Exodus: arming is always an explicit user action.
        if (_active)
            _inputManager.Contexts.SetActiveContext("editor");
        else
            _inputSystem.SetEntityContextActive();
    }

    // Exodus-Start: find the topmost decal under the given coordinates and return its color.
    private bool TryPickDecalColor(EntityCoordinates coords, out Color color)
    {
        color = Color.White;

        var mapPos = _transform.ToMapCoordinates(coords);
        if (!_mapManager.TryFindGridAt(mapPos, out var gridUid, out _))
            return false;

        if (!TryComp<DecalGridComponent>(gridUid, out var decalGrid))
            return false;

        var localPos = Vector2.Transform(mapPos.Position, _transform.GetInvWorldMatrix(gridUid));
        var chunkIndices = SharedDecalSystem.GetChunkIndices(localPos);

        if (!decalGrid.ChunkCollection.ChunkCollection.TryGetValue(chunkIndices, out var chunk))
            return false;

        Decal? best = null;
        var bestZ = int.MinValue;
        var bestId = 0u;

        foreach (var (id, decal) in chunk.Decals)
        {
            // Decals are drawn as a 1x1 tile with their bottom-left corner at Coordinates.
            if (localPos.X < decal.Coordinates.X || localPos.X >= decal.Coordinates.X + 1f ||
                localPos.Y < decal.Coordinates.Y || localPos.Y >= decal.Coordinates.Y + 1f)
                continue;

            // Match the overlay's draw order: highest ZIndex, then highest id, is on top.
            if (best != null && (decal.ZIndex < bestZ || decal.ZIndex == bestZ && id < bestId))
                continue;

            best = decal;
            bestZ = decal.ZIndex;
            bestId = id;
        }

        if (best == null)
            return false;

        color = best.Color ?? Color.White;
        return true;
    }
    // Exodus-End
}
