using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server.Power.Components;
using Content.Shared._Exodus.Mining.Components;
using Content.Shared._Exodus.Mining;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Physics;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.OreMagnet;

public sealed class OreMagnetSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ThrowingSystem _throwing = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;
    [Dependency] private readonly SharedStorageSystem _storage = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private const float ScanInterval = 0.5f;
    private float _scanTimer;

    // Tracks how many magnets are currently active.
    // Lets Update() exit immediately if all magnets idle.
    private int _activeCount;
    private int _lidOpenCount;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<OreMagnetComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<OreMagnetComponent, StorageInteractAttemptEvent>(OnStorageInteractAttempt);
        SubscribeLocalEvent<OreMagnetComponent, ComponentShutdown>(OnMagnetShutdown);
    }

    // Signal handling

    private void OnSignalReceived(Entity<OreMagnetComponent> ent, ref SignalReceivedEvent args)
    {
        if (args.Port != ent.Comp.OnPort)
            return;
        if (ent.Comp.IsActive)
            return;
        if (!TryComp<ApcPowerReceiverComponent>(ent, out var power) || !power.Powered)
            return;

        ent.Comp.DeactivateAt = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.ActivationDuration);
        _activeCount++;
    }

    // Storage power gate

    private void OnStorageInteractAttempt(Entity<OreMagnetComponent> ent, ref StorageInteractAttemptEvent args)
    {
        if (!TryComp<ApcPowerReceiverComponent>(ent, out var power) || !power.Powered)
            args.Cancelled = true;
    }

    // Cleanup when entity is deleted while active

    private void OnMagnetShutdown(EntityUid uid, OreMagnetComponent comp, ComponentShutdown args)
    {
        if (comp.IsActive)
            _activeCount--;
        if (comp.LidCloseAt.HasValue)
            _lidOpenCount--;
    }

    // Per-frame update

    public override void Update(float frameTime)
    {
        _scanTimer -= frameTime;

        // Fast path: no magnets are active and scan isn't due — nothing to do.
        if (_activeCount <= 0 && _lidOpenCount <= 0 && _scanTimer > 0f)
            return;

        if (_activeCount > 0 || _lidOpenCount > 0)
        {
            var timerQuery = EntityQueryEnumerator<OreMagnetComponent>();
            while (timerQuery.MoveNext(out var uid, out var comp))
            {
                if (comp.IsActive && _timing.CurTime >= comp.DeactivateAt!.Value)
                {
                    comp.DeactivateAt = null;
                    _activeCount--;
                }

                if (comp.LidCloseAt.HasValue && _timing.CurTime >= comp.LidCloseAt.Value)
                {
                    comp.LidCloseAt = null;
                    _lidOpenCount--;
                    _appearance.SetData(uid, OreMagnetVisuals.Active, false);
                    if (TryComp<StorageComponent>(uid, out var storageComp))
                        _audio.PlayPvs(storageComp.StorageCloseSound, uid);
                }
            }
        }

        if (_scanTimer > 0f)
            return;
        _scanTimer = ScanInterval;

        PullEntities();
    }

    // Pull + collect logic

    private void PullEntities()
    {
        var magnets = new List<(EntityUid Uid, OreMagnetComponent Comp, Vector2 Pos, MapId MapId)>();

        var magnetQuery = EntityQueryEnumerator<OreMagnetComponent, TransformComponent, ApcPowerReceiverComponent>();
        while (magnetQuery.MoveNext(out var uid, out var comp, out var xform, out var power))
        {
            if (!comp.IsActive || !power.Powered)
                continue;
            magnets.Add((uid, comp, _transform.GetWorldPosition(xform), xform.MapID));
        }

        if (magnets.Count == 0)
            return;

        // For each entity in range, assign it to the nearest magnet with clear LoS.
        // Prevents two active magnets from fighting over the same ore.
        var pullTargets = new Dictionary<EntityUid, (EntityUid MagnetUid, OreMagnetComponent MagnetComp, float Distance)>();

        foreach (var (magnetUid, comp, magnetPos, mapId) in magnets)
        {
            var entities = new HashSet<EntityUid>();
            _lookup.GetEntitiesInRange(mapId, magnetPos, comp.Radius, entities, LookupFlags.Dynamic | LookupFlags.Sundries);

            foreach (var entityUid in entities)
            {
                if (entityUid == magnetUid)
                    continue;
                if (comp.Whitelist != null && !_whitelist.IsValid(comp.Whitelist, entityUid))
                    continue;

                var entityPos = _transform.GetWorldPosition(Transform(entityUid));
                var distance = (entityPos - magnetPos).Length();

                if (pullTargets.TryGetValue(entityUid, out var existing) && existing.Distance <= distance)
                    continue;
                if (!HasLineOfSight(magnetPos, entityPos, mapId, magnetUid))
                    continue;

                pullTargets[entityUid] = (magnetUid, comp, distance);
            }
        }

        foreach (var (entityUid, (magnetUid, magnetComp, distance)) in pullTargets)
        {
            if (distance <= magnetComp.PickupRadius)
            {
                // Close enough: collect directly into storage.
                // If storage is full the item stays on the floor without being re-thrown.
                if (_storage.Insert(magnetUid, entityUid, out _, playSound: false))
                {
                    var wasOpen = magnetComp.LidCloseAt.HasValue;
                    if (!wasOpen)
                    {
                        _lidOpenCount++;
                        if (TryComp<StorageComponent>(magnetUid, out var storageComp))
                            _audio.PlayPvs(storageComp.StorageOpenSound, magnetUid);
                        _appearance.SetData(magnetUid, OreMagnetVisuals.Active, true);
                    }
                    magnetComp.LidCloseAt = _timing.CurTime + TimeSpan.FromSeconds(ScanInterval + 0.25f);
                }
                continue;
            }

            var magnetPos = _transform.GetWorldPosition(Transform(magnetUid));
            var entityPos = _transform.GetWorldPosition(Transform(entityUid));

            _throwing.TryThrow(
                entityUid,
                magnetPos - entityPos,
                magnetComp.PullSpeed,
                magnetUid,
                recoil: false,
                compensateFriction: true,
                doSpin: false,
                animated: false,
                playSound: false);
        }
    }

    //LoS check

    private bool HasLineOfSight(Vector2 from, Vector2 to, MapId mapId, EntityUid ignored)
    {
        var diff = to - from;
        var distance = diff.Length();
        if (distance < 0.5f)
            return true;

        var ray = new CollisionRay(from, diff / distance, (int) CollisionGroup.Impassable);
        return !_physics.IntersectRay(mapId, ray, distance, ignored, returnOnFirstHit: true).Any();
    }
}
