using System.Numerics;
using Content.Server._Exodus.ShipShields; // Exodus layered ship shields
using Content.Server.Power.Components;
using Content.Shared._Crescent.ShipShields;
using Content.Shared._Exodus.ShipShields; // Exodus directional shields
using Content.Shared.Physics; // Exodus directional shields
using Content.Shared.Power; // Exodus shield overload causes
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths; // Exodus directional shields
using Robust.Shared.Physics; // Exodus directional shields
using Robust.Shared.Physics.Collision.Shapes; // Exodus directional shields
using Robust.Shared.Physics.Components; // Exodus directional shields
using Robust.Shared.Physics.Events; // Exodus directional shields
using Robust.Shared.Timing; // Exodus shield overload handling

namespace Content.Server._Crescent.ShipShields;

public sealed partial class ShipShieldsSystem
{
    private const int DirectionalShieldMinimumSegments = 16; // Exodus directional shields
    private const int DirectionalShieldMaximumSegments = 256; // Exodus directional shields
    [Dependency] private readonly IGameTiming _timing = default!; // Exodus shield overload handling

    // Exodus-begin | shield hit absorption, overload causes and directional shield rotation
    private void InitializeShieldHitAbsorption()
    {
        SubscribeLocalEvent<ShipShieldedComponent, ShipShieldHitAttemptEvent>(OnShipShieldHitAttempt);
        SubscribeLocalEvent<DirectionalShipShieldEmitterComponent, MoveEvent>(OnDirectionalShieldEmitterMoved);
        SubscribeLocalEvent<ShipShieldEmitterComponent, PowerChangedEvent>(OnShieldEmitterPowerChanged);
    }

    private void OnShieldEmitterPowerChanged(Entity<ShipShieldEmitterComponent> ent, ref PowerChangedEvent args)
    {
        if (args.Powered)
        {
            ent.Comp.PowerLossReported = false;
            return;
        }

        if (ent.Comp.PowerLossReported)
            return;

        ent.Comp.PowerLossReported = true;
        if (ent.Comp.Shield is null)
            return;

        var overloadAttempt = new ShipShieldOverloadAttemptEvent(
            ShipShieldOverloadCause.PowerLoss,
            false);
        RaiseLocalEvent(ent.Owner, ref overloadAttempt);
    }

    private void OnShipShieldHitAttempt(EntityUid grid, ShipShieldedComponent shielded, ref ShipShieldHitAttemptEvent args)
    {
        if (args.Absorbed)
            return;

        if (!IsPointInsideShield(grid, shielded, args.Point))
            return;

        if (!TryApplyShieldLoad(shielded, args.LoadWatts))
            return;

        args.Absorbed = true;
    }

    private void OnDirectionalShieldEmitterMoved(Entity<DirectionalShipShieldEmitterComponent> ent, ref MoveEvent args)
    {
        if (args.OldRotation.EqualsApprox(args.NewRotation) ||
            !TryComp<ShipShieldEmitterComponent>(ent, out var emitter) ||
            emitter.Shield is not { } shield ||
            emitter.Shielded is not { } grid ||
            Deleted(shield) ||
            !_mapGridQuery.TryGetComponent(grid, out var mapGrid) ||
            !_shieldVisualsQuery.TryGetComponent(shield, out var visuals) ||
            !TryComp<PhysicsComponent>(shield, out var shieldPhysics))
        {
            return;
        }

        _fixtureSystem.DestroyFixture(shield, "shield", updates: false, body: shieldPhysics);

        GenerateDirectionalShieldVisualFixture(
            shield,
            shieldPhysics,
            mapGrid,
            visuals.Padding,
            ent.Comp,
            args.NewRotation);
        UpdateDirectionalShieldField(shield, ent.Comp, args.NewRotation);
        _fixtureSystem.FixtureUpdate(shield, body: shieldPhysics);
        _physicsSystem.WakeBody(shield, body: shieldPhysics);
    }

    private bool IsPointInsideShield(EntityUid grid, ShipShieldedComponent shielded, MapCoordinates point)
    {
        if (!_mapGridQuery.TryGetComponent(grid, out var mapGrid) ||
            !_transformQuery.TryGetComponent(grid, out var xform) ||
            xform.MapID != point.MapId)
        {
            return false;
        }

        var padding = _shieldVisualsQuery.TryGetComponent(shielded.Shield, out var visuals)
            ? visuals.Padding
            : 0f;

        var localPoint = Vector2.Transform(point.Position, _transformSystem.GetInvWorldMatrix(xform));
        var center = mapGrid.LocalAABB.Center;
        var halfWidth = (mapGrid.LocalAABB.Width + padding) * 0.5f;
        var halfHeight = (mapGrid.LocalAABB.Height + padding) * 0.5f;

        if (halfWidth <= 0f || halfHeight <= 0f)
            return false;

        var dx = (localPoint.X - center.X) / halfWidth;
        var dy = (localPoint.Y - center.Y) / halfHeight;
        if (dx * dx + dy * dy > 1f)
            return false;

        if (_directionalShieldFieldQuery.TryGetComponent(shielded.Shield, out var directional) &&
            !IsPointInsideDirectionalShieldArc(localPoint, center, directional))
        {
            return false;
        }

        return true;
    }
    // Exodus-end

    // Exodus-begin directional shield geometry
    private void GenerateDirectionalShieldFixtures(
        EntityUid shield,
        PhysicsComponent shieldPhysics,
        MapGridComponent mapGrid,
        float padding,
        DirectionalShipShieldEmitterComponent directional,
        Angle direction)
    {
        GenerateDirectionalShieldVisualFixture(
            shield,
            shieldPhysics,
            mapGrid,
            padding,
            directional,
            direction);
        GenerateDirectionalShieldCollisionFixture(shield, shieldPhysics, mapGrid, padding);
        UpdateDirectionalShieldField(shield, directional, direction);
        _fixtureSystem.FixtureUpdate(shield, body: shieldPhysics);
    }

    private void GenerateDirectionalShieldVisualFixture(
        EntityUid shield,
        PhysicsComponent shieldPhysics,
        MapGridComponent mapGrid,
        float padding,
        DirectionalShipShieldEmitterComponent directional,
        Angle direction)
    {
        var width = mapGrid.LocalAABB.Width + padding;
        var height = mapGrid.LocalAABB.Height + padding;
        var radius = MathF.Min(width, height) * 0.5f;
        var scaleX = width > height;
        var scale = scaleX ? width / height : height / width;
        var arcRadians = Math.Clamp(directional.ArcDegrees, 1f, 359f) * MathF.PI / 180f;
        var segments = Math.Clamp(
            (int)MathF.Ceiling(radius * 16f * arcRadians / MathF.Tau),
            DirectionalShieldMinimumSegments,
            DirectionalShieldMaximumSegments);
        var step = arcRadians / segments;
        var directionVector = direction.ToWorldVec();
        var directionRadians = MathF.Atan2(directionVector.Y, directionVector.X);
        var start = directionRadians - arcRadians * 0.5f;
        // ChainShape reserves its final vertex as adjacency data instead of creating an edge for it.
        var vertices = new Vector2[segments + 2];

        Vector2 GetArcPoint(float angle, float pointRadius)
        {
            var point = new Vector2(MathF.Cos(angle) * pointRadius, MathF.Sin(angle) * pointRadius);
            if (scaleX)
                point.X *= scale;
            else
                point.Y *= scale;

            return point;
        }

        for (var i = 0; i <= segments + 1; i++)
        {
            vertices[i] = GetArcPoint(start + step * i, radius);
        }

        var previous = GetArcPoint(start - step, radius);
        var next = vertices[^1];

        var shieldChain = new ChainShape();
        shieldChain.CreateChain(vertices, previous, next);
        _fixtureSystem.TryCreateFixture(shield, shieldChain, "shield",
            hard: false,
            collisionLayer: (int)CollisionGroup.BulletImpassable,
            updates: false,
            body: shieldPhysics);
    }

    private void GenerateDirectionalShieldCollisionFixture(
        EntityUid shield,
        PhysicsComponent shieldPhysics,
        MapGridComponent mapGrid,
        float padding)
    {
        var halfWidth = (mapGrid.LocalAABB.Width + padding) * 0.5f;
        var halfHeight = (mapGrid.LocalAABB.Height + padding) * 0.5f;
        Span<Vector2> collisionVertices = stackalloc Vector2[PhysicsConstants.MaxPolygonVertices];
        var step = MathF.Tau / collisionVertices.Length;

        for (var i = 0; i < collisionVertices.Length; i++)
        {
            var angle = step * i;
            collisionVertices[i] = new Vector2(MathF.Cos(angle) * halfWidth, MathF.Sin(angle) * halfHeight);
        }

        var collisionShape = new PolygonShape();
        if (!collisionShape.Set(collisionVertices, collisionVertices.Length))
            return;

        _fixtureSystem.TryCreateFixture(shield, collisionShape, "internalShield",
            hard: true,
            collisionLayer: (int)CollisionGroup.BulletImpassable,
            updates: false,
            body: shieldPhysics);
    }

    private void UpdateDirectionalShieldField(
        EntityUid shield,
        DirectionalShipShieldEmitterComponent directional,
        Angle direction)
    {
        var field = EnsureComp<DirectionalShipShieldFieldComponent>(shield);
        field.ArcDegrees = directional.ArcDegrees;
        field.Direction = direction;
    }

    private bool IsProjectileInsideDirectionalShieldArc(
        EntityUid shield,
        DirectionalShipShieldFieldComponent directional,
        PreventCollideEvent args)
    {
        var projectileVelocity = _physicsSystem.GetMapLinearVelocity(args.OtherEntity, component: args.OtherBody);
        var shieldVelocity = _physicsSystem.GetMapLinearVelocity(shield, component: args.OurBody);
        var worldDirection = _transformSystem.GetWorldRotation(shield, _transformQuery) + directional.Direction;
        return IsIncomingDirectionProtected(worldDirection, directional.ArcDegrees, projectileVelocity - shieldVelocity);
    }

    internal static bool IsIncomingDirectionProtected(
        Angle shieldDirection,
        float arcDegrees,
        Vector2 relativeVelocity)
    {
        var speedSquared = relativeVelocity.LengthSquared();
        if (speedSquared <= float.Epsilon)
            return false;

        var incomingDirection = -relativeVelocity / MathF.Sqrt(speedSquared);
        var arcRadians = Math.Clamp(arcDegrees, 1f, 359f) * MathF.PI / 180f;
        var minimumDot = MathF.Cos(arcRadians * 0.5f);
        return Vector2.Dot(incomingDirection, shieldDirection.ToWorldVec()) >= minimumDot;
    }

    private static bool IsPointInsideDirectionalShieldArc(
        Vector2 point,
        Vector2 center,
        DirectionalShipShieldFieldComponent directional)
    {
        var offset = point - center;
        var lengthSquared = offset.LengthSquared();
        if (lengthSquared <= float.Epsilon)
            return true;

        var arcRadians = Math.Clamp(directional.ArcDegrees, 1f, 359f) * MathF.PI / 180f;
        var minimumDot = MathF.Cos(arcRadians * 0.5f);
        return Vector2.Dot(offset, directional.Direction.ToWorldVec()) >= MathF.Sqrt(lengthSquared) * minimumDot;
    }
    // Exodus-end

    private bool TryApplyShieldLoad(ShipShieldedComponent shielded, float loadWatts)
    {
        if (shielded.Source is not { } source ||
            !_shieldEmitterQuery.TryGetComponent(source, out var emitter))
        {
            return false;
        }

        // Exodus-begin shield damage-overload handling
        var poweredBeforeLoad = _apcPowerReceiverQuery.TryGetComponent(source, out var receiver) && receiver.Powered;

        // Convert added watt load into the emitter's existing Damage accumulator so it shares
        // the same recovery/overload logic as projectile deflection.
        var currentLoad = CalculateLoadDamage(emitter);
        // Keep load above MaxDraw so layered shields can carry overflow into their inner layers.
        var targetLoad = Math.Max(0f, currentLoad + loadWatts);
        emitter.Damage = Math.Max(emitter.Damage, DamageForLoad(emitter, targetLoad));
        // Avoid the regular shield recovery tick immediately eating the same strike.
        emitter.Accumulator = 0f;
        // Exodus-begin layered shield recovery
        if (_layeredShieldQuery.TryGetComponent(source, out var layered))
            layered.RecoveryAccumulator = TimeSpan.Zero;
        // Exodus-end
        var overloadTriggered = targetLoad >= emitter.MaxDraw || IsDamageOverloaded(emitter);
        HandleDamageOverload((source, emitter), poweredBeforeLoad, overloadTriggered);

        if (receiver is not null)
            AdjustEmitterLoad(source, emitter, receiver);
        // Exodus-end

        RaiseShieldStateChanged(Transform(source).GridUid); // Exodus fire-control event-driven UI updates

        return true;
    }

    private static float DamageForLoad(ShipShieldEmitterComponent emitter, float loadWatts)
    {
        if (loadWatts <= 0f)
            return 0f;

        if (emitter.PowerModifier <= 0f || emitter.DamageExp <= 0f)
            return emitter.Damage;

        return MathF.Pow(loadWatts / emitter.PowerModifier, 1f / emitter.DamageExp);
    }

    // Exodus-begin shield overload recovery math
    /// <summary>
    /// Calculates damage retained after an overload safety system activates while keeping both the
    /// hard damage limit and the power-draw limit below their overload thresholds.
    /// </summary>
    public static float CalculateSafeDamageAfterOverload(
        ShipShieldEmitterComponent emitter,
        float retainedDamageFraction)
    {
        var retainedFraction = Math.Clamp(retainedDamageFraction, 0f, 0.99f);
        var safeDamage = Math.Max(0f, emitter.DamageLimit * retainedFraction);

        if (emitter.MaxDraw <= 0f || emitter.PowerModifier <= 0f || emitter.DamageExp <= 0f)
            return safeDamage;

        const float safeLoadFraction = 0.9f;
        var safeLoad = emitter.MaxDraw * safeLoadFraction;
        var safeLoadDamage = DamageForLoad(emitter, safeLoad);
        return Math.Min(safeDamage, safeLoadDamage);
    }

    /// <summary>
    /// Returns the lowest damage value at which either configured overload limit is reached.
    /// </summary>
    internal static float CalculateDamageOverloadThreshold(ShipShieldEmitterComponent emitter)
    {
        var overloadDamage = Math.Max(0f, emitter.DamageLimit);
        if (emitter.MaxDraw <= 0f || emitter.PowerModifier <= 0f || emitter.DamageExp <= 0f)
            return overloadDamage;

        return Math.Min(overloadDamage, DamageForLoad(emitter, emitter.MaxDraw));
    }
    // Exodus-end

    // Exodus-begin layered shield load scaling
    private static float GetDeflectionDamageModifier(
        Entity<ShipShieldEmitterComponent> ent,
        LayeredShipShieldComponent? layered)
    {
        if (layered is null)
            return Math.Max(0f, ent.Comp.DeflectionDamageModifier);

        var maximumLayers = Math.Max(1, layered.LayerCount);
        var activeLayers = Math.Clamp(layered.ActiveLayerCount, 1, maximumLayers);
        var collapsedLayers = maximumLayers - activeLayers;

        return Math.Max(
            0f,
            ent.Comp.DeflectionDamageModifier + collapsedLayers * layered.DeflectionDamageModifierStep);
    }
    // Exodus-end
}
