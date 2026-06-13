using System.Numerics;
using Content.Shared.Shuttles.BUIStates;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Content.Server.Shuttles.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class RadarConsoleSystem : SharedRadarConsoleSystem
{
    [Dependency] private SharedTransformSystem _transform = default!; // Frontier // Mono
    [Dependency] private ShuttleConsoleSystem _console = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RadarConsoleComponent, ComponentStartup>(OnRadarStartup);
        SubscribeLocalEvent<RadarConsoleComponent, BoundUIOpenedEvent>(OnUIOpened); // Frontier
    }

    private void OnRadarStartup(EntityUid uid, RadarConsoleComponent component, ComponentStartup args)
    {
        UpdateState(uid, component);
    }

    // Frontier
    private void OnUIOpened(EntityUid uid, RadarConsoleComponent component, ref BoundUIOpenedEvent args)
    {
        UpdateState(uid, component);
    }
    // End Frontier

    protected override void UpdateState(EntityUid uid, RadarConsoleComponent component)
    {
        var xform = Transform(uid);
        // Mono
        var parentUid = xform.GridUid;
        EntityCoordinates? coordinates = null;
        Angle? angle = null;
        if (component.FollowEntity)
        {
            coordinates = new EntityCoordinates(uid, Vector2.Zero);
            angle = Angle.Zero; // Frontier: Angle.Zero<Angle.FromDegrees(180) // Mono - frontier strikes again
        }
        else if (parentUid is { } parent)
        {
            coordinates = _transform.WithEntityId(xform.Coordinates, parent);
            angle = _transform.GetWorldRotation(xform) - _transform.GetWorldRotation(parent);
        }

        if (_uiSystem.HasUi(uid, RadarConsoleUiKey.Key))
        {
            NavInterfaceState state;
            var docks = _console.GetAllDocks();

            if (coordinates != null && angle != null)
            {
                state = _console.GetNavState(uid, docks, _console.GetAllGrapLinks(), coordinates.Value, angle.Value); // Exodus - ShuttleHooks
            }
            else
            {
                state = _console.GetNavState(uid, docks, _console.GetAllGrapLinks()); // Exodus - ShuttleHooks
            }

            // Frontier: ghost radar restrictions
            if (component.MaxIffRange != null)
                state.MaxIffRange = component.MaxIffRange.Value;
            state.HideCoords = component.HideCoords;
            state.Target = component.Target;
            state.TargetEntity = GetNetEntity(component.TargetEntity);
            state.HideTarget = component.HideTarget;
            // End Frontier

            _uiSystem.SetUiState(uid, RadarConsoleUiKey.Key, new NavBoundUserInterfaceState(state));
        }
    }

    // Frontier: settable waypoints
    public void SetTarget(Entity<RadarConsoleComponent> ent, NetEntity targetEntity, Vector2 target)
    {
        // Try to get entity
        if (EntityManager.TryGetEntity(targetEntity, out var targetUid)
            && HasComp<ShuttleComponent>(targetUid)
            && (!TryComp(targetUid, out IFFComponent? iff) || (iff.Flags & (IFFFlags.Hide | IFFFlags.HideLabel)) == 0)
            && TryComp(targetUid, out TransformComponent? xform))
        {
            ent.Comp.TargetEntity = targetUid;
            ent.Comp.Target = _transform.GetMapCoordinates(xform).Position;
        }
        else
        {
            ent.Comp.Target = target;
            ent.Comp.TargetEntity = null;
        }
        Dirty(ent);
    }

    public void SetHideTarget(Entity<RadarConsoleComponent> ent, bool hideTarget)
    {
        ent.Comp.HideTarget = hideTarget;
        Dirty(ent);
    }
    // End Frontier
}
