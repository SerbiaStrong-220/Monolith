using Content.Shared._Goobstation.CCVars;
using Content.Server.Popups;
using Content.Server._Goobstation.MobCaller;
using Content.Shared.Popups;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Content.Shared.Exodus.CCVar;
using Content.Server.Station.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Goobstation.SpaceWhale.StationProximity;

// used by space whales so think twice beofre using it for yourself somewhere else
// also half of this was taken from wizden #30436 and redone for whale purposes
// Exodus: Logic was redone for Frontier conditions
public sealed class StationProximitySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(60); // le hardcode major
    private TimeSpan _nextCheck = TimeSpan.Zero;
    private float _spawnDistance;

    public override void Initialize()
    {
        base.Initialize();
        _nextCheck = _timing.CurTime + CheckInterval;

        _cfg.OnValueChanged(GoobCVars.SpaceWhaleSpawnDistance, (val) => _spawnDistance = val, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextCheck)
            return;

        _nextCheck = _timing.CurTime + CheckInterval;

        if (!_cfg.GetCVar(GoobCVars.SpaceWhaleSpawn))
            return;

        var centralStationId = _cfg.GetCVar(XCVars.CentralStationId);
        var centralStation = _station.GetStationById(centralStationId);

        if (centralStation is not { } station)
            return;

        var stationXform = Transform(station);

        var humanoidQuery = EntityQueryEnumerator<HumanoidAppearanceComponent, MobStateComponent, TransformComponent>();
        while (humanoidQuery.MoveNext(out var uid, out _, out var mobState, out var humanoidXform))
        {
            var stationPos = _transform.GetWorldPosition(stationXform);
            var humanoidPos = _transform.GetWorldPosition(humanoidXform);
            var vec = stationPos - humanoidPos;

            var isFar = mobState.CurrentState == MobState.Alive &&
                        stationXform.MapUid == humanoidXform.MapUid &&
                        vec.Length() > _spawnDistance;

            if (isFar)
            {
                // far enough, time to spawn
                HandleFarFromStation(uid);
            }
            else
            {
                // if near station, remove the tracking component and delete the dummy entity
                if (TryComp<SpaceWhaleTargetComponent>(uid, out var whaleTarget))
                {
                    QueueDel(whaleTarget.Entity);
                    RemComp<SpaceWhaleTargetComponent>(uid);
                }
            }
        }
    }

    private void HandleFarFromStation(EntityUid entity) // basically handles space whale spawnings
    {
        if (HasComp<SpaceWhaleTargetComponent>(entity))
            return;

        _popup.PopupEntity(
            Loc.GetString("station-proximity-far-from-station"),
            entity,
            entity,
            PopupType.LargeCaution);

        _audio.PlayEntity(new SoundPathSpecifier("/Audio/_Goobstation/Ambience/SpaceWhale/leviathan-appear.ogg"),
            entity,
            entity,
            AudioParams.Default.WithVolume(1f));

        // Spawn a dummy entity at the player's location and lock it onto the player
        var dummy = Spawn(null, Transform(entity).Coordinates);
        _transform.SetParent(dummy, entity);
        var mobCaller = EnsureComp<MobCallerComponent>(dummy); // assign the goidacaller to the dummy

        mobCaller.SpawnProto = _cfg.GetCVar(GoobCVars.SpaceWhalePrototype);
        mobCaller.MaxAlive = 1; // nuh uh
        mobCaller.MinDistance = 600f; // should be far away
        mobCaller.NeedAnchored = false;
        mobCaller.NeedPower = false;
        mobCaller.SpawnSpacing = TimeSpan.FromSeconds(65); // to give the guy some time to get back to the station + prevent him from like, QSI-ing to the station to summon the worm in the station lmao, also bru these 5 seconds are really important

        var targetComp = EnsureComp<SpaceWhaleTargetComponent>(entity); // track the dummy on the player
        targetComp.Entity = dummy;
    }
}
