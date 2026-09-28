// Exodus: configurable nozzles reuse standard power, upgrades, burn damage and omni registration APIs.
using System.Numerics;
using Content.Server._Exodus.Shuttles.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Shared._Exodus.Shuttles;
using Content.Shared.Examine;
using Content.Shared.Localizations;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Shuttles.Components;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ThrusterSystem
{
    private EntityQuery<ThrusterNozzlesComponent> _nozzlesQuery;

    private void InitializeNozzles()
    {
        _nozzlesQuery = GetEntityQuery<ThrusterNozzlesComponent>();
        SubscribeLocalEvent<ThrusterNozzlesComponent, ComponentStartup>(OnNozzlesStartup);
        SubscribeLocalEvent<ThrusterNozzlesComponent, ComponentShutdown>(OnNozzlesShutdown);
        SubscribeLocalEvent<ThrusterNozzleGridComponent, TileChangedEvent>(OnNozzleTilesChanged);
    }

    private void OnNozzlesStartup(Entity<ThrusterNozzlesComponent> ent, ref ComponentStartup args)
    {
        if (TryComp<ThrusterComponent>(ent, out var thruster))
            RefreshNozzleThruster((ent.Owner, thruster, ent.Comp));
    }

    private void OnNozzlesShutdown(Entity<ThrusterNozzlesComponent> ent, ref ComponentShutdown args)
    {
        if (TryComp<ThrusterComponent>(ent, out var thruster))
            DisableNozzleThruster((ent.Owner, thruster, ent.Comp));
        WatchNozzleGrid(ent, null);
    }

    private void OnNozzleTilesChanged(Entity<ThrusterNozzleGridComponent> ent, ref TileChangedEvent args)
    {
        // Tile edits are infrequent. Only engines on this grid are visited, including blocked ones.
        foreach (var uid in ent.Comp.Thrusters)
        {
            if (_nozzlesQuery.TryComp(uid, out var nozzles) && TryComp<ThrusterComponent>(uid, out var thruster))
                RefreshNozzleThruster((uid, thruster, nozzles));
        }
    }

    private void WatchNozzleGrid(Entity<ThrusterNozzlesComponent> ent, EntityUid? grid)
    {
        if (ent.Comp.WatchedGrid == grid)
            return;

        if (TryComp<ThrusterNozzleGridComponent>(ent.Comp.WatchedGrid, out var old))
            old.Thrusters.Remove(ent);

        ent.Comp.WatchedGrid = grid;
        if (grid is { } uid)
            EnsureComp<ThrusterNozzleGridComponent>(uid).Thrusters.Add(ent);
    }

    private static Angle NozzleAngle(ThrusterNozzleDirection direction)
    {
        return Angle.FromDegrees((int) direction * 90);
    }

    private static DirectionFlag NozzleFlag(ThrusterNozzleDirection direction)
    {
        return (DirectionFlag) (1 << (int) direction);
    }

    private static int NozzleWorldIndex(ThrusterNozzleDirection direction, Angle rotation)
    {
        return (int) (rotation + NozzleAngle(direction)).GetCardinalDir() / 2;
    }

    private static string NozzleFixture(ThrusterNozzleDirection direction)
    {
        return direction switch
        {
            ThrusterNozzleDirection.South => "thruster-burn-south",
            ThrusterNozzleDirection.East => "thruster-burn-east",
            ThrusterNozzleDirection.North => "thruster-burn-north",
            ThrusterNozzleDirection.West => "thruster-burn-west",
            _ => throw new ArgumentOutOfRangeException(nameof(direction)),
        };
    }

    private bool NozzleExposed(TransformComponent xform, ThrusterNozzleDirection direction, ThrusterNozzle nozzle)
    {
        if (!TryComp<MapGridComponent>(xform.GridUid, out var grid))
            return true;

        var angle = xform.LocalRotation + NozzleAngle(direction);
        foreach (var offset in nozzle.SpaceCheckOffsets)
        {
            var position = xform.LocalPosition + angle.RotateVec(offset);
            var tile = new Vector2i((int) Math.Floor(position.X), (int) Math.Floor(position.Y));
            if (!_turf.IsSpace(_mapSystem.GetTileRef(xform.GridUid.Value, grid, tile)))
                return false;
        }

        return true;
    }

    private DirectionFlag GetAvailableNozzles(Entity<ThrusterComponent, ThrusterNozzlesComponent> ent, TransformComponent xform)
    {
        if (!ent.Comp1.Enabled || ent.Comp1.LifeStage > ComponentLifeStage.Running ||
            ent.Comp2.LifeStage > ComponentLifeStage.Running || !xform.Anchored || !this.IsPowered(ent, EntityManager))
        {
            return DirectionFlag.None;
        }

        var result = DirectionFlag.None;
        foreach (var (direction, nozzle) in ent.Comp2.Nozzles)
        {
            if (!ent.Comp1.RequireSpace || NozzleExposed(xform, direction, nozzle))
                result |= NozzleFlag(direction);
        }

        return result;
    }

    private void RefreshNozzleThruster(Entity<ThrusterComponent, ThrusterNozzlesComponent> ent)
    {
        var xform = Transform(ent);
        WatchNozzleGrid((ent.Owner, ent.Comp2), xform.Anchored ? xform.GridUid : null);
        var available = GetAvailableNozzles(ent, xform);
        var directions = DirectionFlag.None;
        foreach (var direction in ent.Comp2.Nozzles.Keys)
        {
            if ((available & NozzleFlag(direction)) != 0)
                directions |= (DirectionFlag) (1 << NozzleWorldIndex(direction, xform.LocalRotation));
        }

        if (ent.Comp1.IsOn && ent.Comp2.RegisteredGrid == xform.GridUid &&
            ent.Comp2.RegisteredDirections == directions && ent.Comp2.AvailableNozzles == available &&
            ent.Comp2.RegisteredThrust == ent.Comp1.Thrust && ent.Comp2.RegisteredBaseThrust == ent.Comp1.BaseThrust)
        {
            UpdateNozzleFiring(ent);
            return;
        }

        DisableNozzleThruster(ent);
        if (available == DirectionFlag.None || !TryComp<ShuttleComponent>(xform.GridUid, out var shuttle))
            return;

        ent.Comp1.IsOn = true;
        ent.Comp2.AvailableNozzles = available;
        ent.Comp2.RegisteredGrid = xform.GridUid;
        ent.Comp2.RegisteredDirections = directions;
        ent.Comp2.RegisteredThrust = ent.Comp1.Thrust;
        ent.Comp2.RegisteredBaseThrust = ent.Comp1.BaseThrust;

        for (var i = 0; i < 4; i++)
        {
            if ((directions & (DirectionFlag) (1 << i)) != 0)
                AddLinearThrust(xform.GridUid.Value, shuttle, i, ent.Comp1.Thrust, ent.Comp1.BaseThrust, ent, raiseEvent: false);
        }

        if (TryComp<PhysicsComponent>(ent, out var physics) && ent.Comp1.BurnPoly.Count > 0)
        {
            foreach (var direction in ent.Comp2.Nozzles.Keys)
            {
                if ((available & NozzleFlag(direction)) == 0)
                    continue;

                var vertices = new List<Vector2>(ent.Comp1.BurnPoly.Count);
                var rotation = NozzleAngle(direction);
                foreach (var vertex in ent.Comp1.BurnPoly)
                    vertices.Add(rotation.RotateVec(vertex));
                var shape = new PolygonShape();
                shape.Set(vertices);
                _fixtureSystem.TryCreateFixture(ent, shape, NozzleFixture(direction), hard: false,
                    collisionLayer: (int) CollisionGroup.FullTileMask, body: physics);
            }
        }

        _appearance.SetData(ent, ThrusterVisualState.State, true);
        if (_light.TryGetLight(ent, out var light))
            _light.SetEnabled(ent, true, light);
        _ambient.SetAmbience(ent, true);
        UpdateNozzleFiring(ent);
        RefreshCenter(ent, shuttle);
        NotifyLinearThrustChanged(xform.GridUid);
    }

    private void DisableNozzleThruster(Entity<ThrusterComponent, ThrusterNozzlesComponent> ent)
    {
        var grid = ent.Comp2.RegisteredGrid;
        if (TryComp<ShuttleComponent>(grid, out var shuttle))
        {
            for (var i = 0; i < 4; i++)
            {
                if ((ent.Comp2.RegisteredDirections & (DirectionFlag) (1 << i)) != 0)
                {
                    RemoveLinearThrust(grid.Value, shuttle, i, ent.Comp2.RegisteredThrust,
                        ent.Comp2.RegisteredBaseThrust, ent, raiseEvent: false);
                }
            }

            RefreshCenter(ent, shuttle);
            NotifyLinearThrustChanged(grid);
        }

        ent.Comp1.IsOn = false;
        ent.Comp1.Firing = false;
        ent.Comp2.RegisteredGrid = null;
        ent.Comp2.RegisteredDirections = DirectionFlag.None;
        ent.Comp2.AvailableNozzles = DirectionFlag.None;
        ent.Comp2.FiringNozzles = DirectionFlag.None;
        ent.Comp1.Colliding.Clear();

        foreach (var (direction, nozzle) in ent.Comp2.Nozzles)
        {
            _fixtureSystem.DestroyFixture(ent, NozzleFixture(direction));
            nozzle.Colliding.Clear();
            _appearance.SetData(ent, direction, false);
        }

        _appearance.SetData(ent, ThrusterVisualState.State, false);
        _appearance.SetData(ent, ThrusterVisualState.Thrusting, false);
        if (_light.TryGetLight(ent, out var light))
            _light.SetEnabled(ent, false, light);
        _ambient.SetAmbience(ent, false);
    }

    private void UpdateNozzleFiring(Entity<ThrusterComponent, ThrusterNozzlesComponent> ent)
    {
        var firing = DirectionFlag.None;
        if (ent.Comp1.IsOn && TryComp<ShuttleComponent>(ent.Comp2.RegisteredGrid, out var shuttle))
        {
            var rotation = Transform(ent).LocalRotation;
            foreach (var direction in ent.Comp2.Nozzles.Keys)
            {
                var flag = NozzleFlag(direction);
                var worldFlag = (DirectionFlag) (1 << NozzleWorldIndex(direction, rotation));
                if ((ent.Comp2.AvailableNozzles & flag) != 0 && (shuttle.ThrustDirections & worldFlag) != 0)
                    firing |= flag;
            }
        }

        ent.Comp2.FiringNozzles = firing;
        ent.Comp1.Firing = firing != DirectionFlag.None;
        foreach (var direction in ent.Comp2.Nozzles.Keys)
            _appearance.SetData(ent, direction, (firing & NozzleFlag(direction)) != 0);
        _appearance.SetData(ent, ThrusterVisualState.Thrusting, ent.Comp1.Firing);
        RefreshNozzleCollisions(ent);
    }

    private void RefreshNozzleCollisions(Entity<ThrusterComponent, ThrusterNozzlesComponent> ent)
    {
        // Reuse the standard damage loop, with a deduplicated list of entities in firing nozzles.
        ent.Comp1.Colliding.Clear();
        foreach (var (direction, nozzle) in ent.Comp2.Nozzles)
        {
            if ((ent.Comp2.FiringNozzles & NozzleFlag(direction)) == 0)
                continue;
            foreach (var uid in nozzle.Colliding)
            {
                if (!Deleted(uid) && !ent.Comp1.Colliding.Contains(uid))
                    ent.Comp1.Colliding.Add(uid);
            }
        }
    }

    private void OnNozzleStartCollide(Entity<ThrusterComponent, ThrusterNozzlesComponent> ent, ref StartCollideEvent args)
    {
        foreach (var (direction, nozzle) in ent.Comp2.Nozzles)
        {
            if (args.OurFixtureId != NozzleFixture(direction))
                continue;
            nozzle.Colliding.Add(args.OtherEntity);
            RefreshNozzleCollisions(ent);
            return;
        }
    }

    private void OnNozzleEndCollide(Entity<ThrusterComponent, ThrusterNozzlesComponent> ent, ref EndCollideEvent args)
    {
        foreach (var (direction, nozzle) in ent.Comp2.Nozzles)
        {
            if (args.OurFixtureId != NozzleFixture(direction))
                continue;
            nozzle.Colliding.Remove(args.OtherEntity);
            RefreshNozzleCollisions(ent);
            return;
        }
    }

    private void ExamineNozzles(Entity<ThrusterNozzlesComponent> ent, ExaminedEvent args)
    {
        var xform = Transform(ent);
        if (!xform.Anchored)
            return;
        foreach (var (direction, nozzle) in ent.Comp.Nozzles)
        {
            var angle = xform.LocalRotation + NozzleAngle(direction);
            var name = ContentLocalizationManager.FormatDirection(angle.Opposite().ToWorldVec().GetDir()).ToLower();
            args.PushMarkup(Loc.GetString("thruster-comp-nozzle-direction", ("direction", name)));
            args.PushMarkup(Loc.GetString(NozzleExposed(xform, direction, nozzle)
                ? "thruster-comp-nozzle-exposed"
                : "thruster-comp-nozzle-not-exposed"));
        }
    }
}
