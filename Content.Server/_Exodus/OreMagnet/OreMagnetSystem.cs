using System.Numerics;
using Content.Server.Power.Components;
using Content.Shared._Exodus.Mining;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Throwing;
using Content.Shared.Whitelist;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Exodus.OreMagnet;

public sealed class OreMagnetSystem : EntitySystem
{
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    private EntityQuery<StorageComponent> _storageQuery;
    private readonly List<(Entity<OreMagnetComponent> Magnet, Vector2 Pos, MapId MapId)> _magnets = new();
    private readonly Dictionary<EntityUid, (Entity<OreMagnetComponent> Magnet, float Distance)> _pullTargets = new();
    private readonly HashSet<Entity<ItemComponent>> _lookupEnts = new();

    private const float ScanInterval = 1f;
    private float _scanTimer;

    // Tracks how many magnets are currently active.
    // Lets Update() exit immediately if all magnets idle.
    private int _activeCount;
    private int _lidOpenCount;

    public override void Initialize()
    {
        base.Initialize();
        _storageQuery = GetEntityQuery<StorageComponent>();
        SubscribeLocalEvent<OreMagnetComponent, SignalReceivedEvent>(OnSignalReceived);
        SubscribeLocalEvent<OreMagnetComponent, StorageInteractAttemptEvent>(OnStorageInteractAttempt);
        SubscribeLocalEvent<OreMagnetComponent, ComponentShutdown>(OnMagnetShutdown);
        SubscribeLocalEvent<OreMagnetComponent, ThrowHitByEvent>(OnHitByThrown);
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
        if (_activeCount <= 0 && _lidOpenCount <= 0)
            return;

        if (_activeCount > 0 || _lidOpenCount > 0)
        {
            var timerQuery = EntityQueryEnumerator<OreMagnetComponent>();
            while (timerQuery.MoveNext(out var uid, out var comp))
            {
                if (comp.IsActive && _timing.CurTime >= comp.DeactivateAt)
                    DeactivateMagnet((uid, comp));

                if (comp.LidCloseAt.HasValue && _timing.CurTime >= comp.LidCloseAt.Value)
                {
                    comp.LidCloseAt = null;
                    _lidOpenCount--;
                    _appearance.SetData(uid, OreMagnetVisuals.Active, false);
                    if (_storageQuery.TryComp(uid, out var storageComp))
                        _audio.PlayPvs(storageComp.StorageCloseSound, uid);
                }
            }
        }

        _scanTimer += frameTime;
        if (_scanTimer < ScanInterval)
            return;
        _scanTimer = 0;

        PullEntities();
    }

    // Pull + collect logic

    private void PullEntities()
    {
        if (_activeCount <= 0)
            return;

        _magnets.Clear();

        var magnetQuery = EntityQueryEnumerator<OreMagnetComponent, TransformComponent, ApcPowerReceiverComponent>();
        while (magnetQuery.MoveNext(out var uid, out var comp, out var xform, out var power))
        {
            if (!comp.IsActive || !power.Powered)
                continue;
            _magnets.Add(((uid, comp), _transform.GetWorldPosition(xform), xform.MapID));
        }

        if (_magnets.Count == 0)
            return;

        // For each entity in range, assign it to the nearest magnet.
        // Prevents two active magnets from fighting over the same ore.
        _pullTargets.Clear();

        foreach (var (magnet, magnetPos, mapId) in _magnets)
        {
            var (magnetUid, comp) = magnet;
            _lookupEnts.Clear();
            _lookup.GetEntitiesInRange(mapId, magnetPos, comp.Radius, _lookupEnts, LookupFlags.Dynamic | LookupFlags.Sundries);

            foreach (var ent in _lookupEnts)
            {
                var entityUid = ent.Owner;
                if (entityUid == magnetUid)
                    continue;
                if (_whitelist.IsWhitelistFail(comp.Whitelist, entityUid))
                    continue;

                var entityPos = _transform.GetWorldPosition(Transform(entityUid));
                var distance = (entityPos - magnetPos).Length();

                if (_pullTargets.TryGetValue(entityUid, out var existing) && existing.Distance <= distance)
                    continue;
                if (!_interaction.InRangeUnobstructed(magnetUid, entityUid, comp.Radius))
                    continue;

                _pullTargets[entityUid] = ((magnetUid, comp), distance);
            }
        }

        foreach (var (entityUid, (magnet, _)) in _pullTargets)
        {
            var (magnetUid, magnetComp) = magnet;

            if (!magnetComp.IsActive)
                continue;

            if (!_storage.CanInsert(magnetUid, entityUid, out _))
            {
                // New checks of IsActive is way cheaper that repeating CanInsert on full magnet
                DeactivateMagnet(magnet);
                continue;
            }

            var magnetPos = _transform.GetWorldPosition(Transform(magnetUid));
            var entityPos = _transform.GetWorldPosition(Transform(entityUid));
            var direction = magnetPos - entityPos;

            if (direction == Vector2.Zero)
                continue;

            _throwing.TryThrow(
                entityUid,
                direction,
                magnetComp.PullSpeed,
                recoil: false,
                compensateFriction: true,
                doSpin: false,
                animated: false,
                playSound: false);
        }
    }

    private void DeactivateMagnet(Entity<OreMagnetComponent> magnet)
    {
        if (!magnet.Comp.IsActive)
            return;

        magnet.Comp.DeactivateAt = null;
        _activeCount--;
    }

    // Physics collision — ore hits the machine after being thrown toward it
    private void OnHitByThrown(Entity<OreMagnetComponent> ent, ref ThrowHitByEvent args)
    {
        if (!ent.Comp.IsActive)
            return;
        if (!TryComp<ApcPowerReceiverComponent>(ent, out var power) || !power.Powered)
            return;
        if (ent.Comp.Whitelist != null && !_whitelist.IsValid(ent.Comp.Whitelist, args.Thrown))
            return;
        if (!_storage.Insert(ent, args.Thrown, out _, playSound: false))
            return;

        var hadTimer = ent.Comp.LidCloseAt.HasValue;
        if (!hadTimer)
            _lidOpenCount++;

        ent.Comp.LidCloseAt = _timing.CurTime + TimeSpan.FromSeconds(1.0);

        if (!hadTimer)
        {
            if (_storageQuery.TryComp(ent, out var storageComp))
                _audio.PlayPvs(storageComp.StorageOpenSound, ent);
            _appearance.SetData(ent, OreMagnetVisuals.Active, true);
        }
    }
}
