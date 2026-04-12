using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server.Roles;
using Content.Server.Shuttles.Components;
using Content.Shared._Exodus.CCVar;
using Content.Shared.Database;
using Robust.Server.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.AutoUnstuck;

public sealed class AutoUnstuckSystem : EntitySystem
{
    private static readonly Vector2[] StuckOffsets =
    {
        new(1f, 0f),
        new(1f, 1f),
        new(0f, 1f),
        new(-1f, 0f),
        new(-1f, -1f),
        new(-1f, 1f),
        new(1f, -1f),
        new(0f, -1f),
    };

    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly IAdminLogManager _adminLog = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly TransformSystem _transform = default!;

    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<StuckedComponent> _stuckedQuery;
    private EntityQuery<MapComponent> _mapQuery;
    private EntityQuery<MapGridComponent> _mapGridQuery;
    private EntityQuery<ShuttleComponent> _shuttleQuery;

    private TimeSpan _unstuckTime;

    private float _timer;
    private const float UpdateTimer = 1f;

    private List<Entity<PhysicsComponent, TransformComponent>> _bodies = new();

    public override void Initialize()
    {
        base.Initialize();

        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _stuckedQuery = GetEntityQuery<StuckedComponent>();
        _mapQuery = GetEntityQuery<MapComponent>();
        _mapGridQuery = GetEntityQuery<MapGridComponent>();
        _shuttleQuery = GetEntityQuery<ShuttleComponent>();

        Subs.CVar(_config, XCVars.AutoUnstuckTime, val => _unstuckTime = TimeSpan.FromSeconds(val), true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _timer += frameTime;
        if (_timer < UpdateTimer)
            return;
        _timer = 0;

        _bodies.Clear();

        foreach (var ent in _physics.AwakeBodies)
        {
            _bodies.Add(ent);
        }

        foreach (var ent in _bodies)
        {
            var (uid, body, xform) = ent;

            if (IsPaused(uid))
                continue;

            if (!_fixturesQuery.TryComp(uid, out var fixtures))
                continue;

            if (_mapGridQuery.HasComp(uid) || _mapQuery.HasComp(uid) || _shuttleQuery.HasComp(uid)) continue;

            if (body.BodyType == BodyType.Static || !body.CanCollide)
                continue;

            var pos = _transform.GetWorldPosition(xform);
            var hasHardContact = false;
            var dirSum = Vector2.Zero;

            var contacts = _physics.GetContacts((uid, fixtures));

            while (contacts.MoveNext(out var contact))
            {
                if (!contact.IsTouching || !contact.Hard)
                    continue;

                var other = contact.OtherEnt(uid);
                var otherBody = contact.OtherBody(uid);

                if (otherBody.BodyType != BodyType.Static)
                    continue;

                var otherPos = _transform.GetWorldPosition(other);
                var vec = pos - otherPos;

                if (vec != Vector2.Zero)
                    dirSum += Vector2.Normalize(vec);

                hasHardContact = true;
            }

            if (!hasHardContact)
            {
                RemCompDeferred<StuckedComponent>(uid);
                continue;
            }

            if (!_stuckedQuery.TryComp(uid, out var stucked))
            {
                stucked = AddComp<StuckedComponent>(uid);
                stucked.StuckedAt = _timing.CurTime;
                continue;
            }

            if (stucked.StuckedAt + _unstuckTime > _timing.CurTime)
                continue;

            var dir = dirSum == Vector2.Zero ? Vector2.Zero : Vector2.Normalize(dirSum);
            var offset = dir.Length() < 0.05f ? _random.Pick(StuckOffsets) : dir;

            _physics.SetCanCollide(uid, false, manager: fixtures, body: body);
            _transform.SetWorldPosition(uid, pos + offset);
            _physics.SetCanCollide(uid, true, manager: fixtures, body: body);
            _physics.SetLinearVelocity(uid, Vector2.Zero, manager: fixtures, body: body);
            _physics.WakeBody(uid, manager: fixtures, body: body);

            _adminLog.Add(LogType.AutoUnstuck, LogImpact.Low, $"{ToPrettyString(uid)} was detected as stucked at {xform.Coordinates} and automatically unstucked");
        }
    }
}
