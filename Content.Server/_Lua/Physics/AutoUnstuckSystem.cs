// Taken from https://github.com/Lua-Frontier/sector-frontier-14/blob/ce58b104087fe158e8e3f1399bd0fc19a4019fda/Content.Server/_Lua/Physics/AutoUnstuckSystem.cs
// LuaWorld - This file is licensed under AGPLv3
// Copyright (c) 2025 LuaWorld
// See AGPLv3.txt for details.

using System.Numerics;
using JetBrains.Annotations;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Random;
using Content.Shared.Mobs.Components;

namespace Content.Server._Lua.Physics;

[UsedImplicitly]
public sealed class AutoUnstuckSystem : EntitySystem
{
    private static readonly Vector2[] StuckOffsets =
    {
        new(2f, 0f),
        new(-2f, 0f),
        new(0f, 2f),
        new(0f, -2f),
    };

    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;
    [Dependency] private readonly IRobustRandom _random = default!;

    private readonly Dictionary<EntityUid, float> _stuckTime = new();
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;

    public override void Initialize()
    {
        base.Initialize();

        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MobStateComponent>();

        while (query.MoveNext(out var uid, out _))
        {
            if (IsPaused(uid))
                continue;

            if (!_physicsQuery.TryGetComponent(uid, out var body))
                continue;

            if (!_fixturesQuery.TryGetComponent(uid, out var fixtures))
                continue;

            if (body.BodyType == BodyType.Static || !body.CanCollide)
                continue;

            var xform = Transform(uid);

            var hasStaticHardContact = false;
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

                var selfTx = _physics.GetPhysicsTransform(uid, xform);
                var otherTx = _physics.GetPhysicsTransform(other, xform);
                var vec = selfTx.Position - otherTx.Position;

                if (vec != Vector2.Zero)
                    dirSum += Vector2.Normalize(vec);

                hasStaticHardContact = true;
            }

            if (!hasStaticHardContact)
            {
                _stuckTime.Remove(uid);
                continue;
            }

            if (_stuckTime.TryGetValue(uid, out var t))
                _stuckTime[uid] = t + frameTime;
            else
                _stuckTime[uid] = frameTime;

            if (_stuckTime[uid] < 15f)
                continue;

            var dir = dirSum.Normalized();

            var offset = dir.Length() < 0.05f ? _random.Pick(StuckOffsets) : dir;
            _physics.SetCanCollide(uid, false, manager: fixtures, body: body);
            _xform.SetCoordinates(uid, xform, xform.Coordinates.Offset(offset));
            _physics.SetCanCollide(uid, true, manager: fixtures, body: body);
            _physics.SetLinearVelocity(uid, Vector2.Zero, manager: fixtures, body: body);
            _physics.WakeBody(uid, manager: fixtures, body: body);

            _stuckTime.Remove(uid);
        }
    }
}
