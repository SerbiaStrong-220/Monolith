using System.Numerics;
using Content.Shared._Mono.Radar;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Client._Mono.Radar;

public sealed partial class RadarBlipsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IMapManager _map = default!;
    [Dependency] private SharedTransformSystem _xform = default!;

    private const double BlipStaleSeconds = 3.0;
    private TimeSpan _lastRequestTime = TimeSpan.Zero;
    private TimeSpan _lastNebulaMapRequestTime = TimeSpan.Zero; // Exodus nebula-ftl-map
    private MapId _lastNebulaMapRequest = MapId.Nullspace; // Exodus nebula-ftl-map
    private static readonly TimeSpan RequestThrottle = TimeSpan.FromMilliseconds(500);

    private TimeSpan _lastUpdatedTime;
    private TimeSpan _lastNebulaMapUpdatedTime; // Exodus nebula-ftl-map
    private List<BlipNetData> _blips = new();
    private List<BlipNetData> _nebulaMapBlips = new(); // Exodus nebula-ftl-map
    private List<HitscanNetData> _hitscans = new();
    private List<BlipConfig> _configPalette = new();
    private List<BlipConfig> _nebulaMapConfigPalette = new(); // Exodus nebula-ftl-map

    // cached results to avoid allocating on every draw/frame
    private readonly List<BlipData> _cachedBlipData = new();
    private readonly List<BlipData> _cachedNebulaMapBlipData = new(); // Exodus nebula-ftl-map

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<GiveBlipsEvent>(HandleReceiveBlips);
        SubscribeNetworkEvent<BlipRemovalEvent>(RemoveBlip);
    }

    private void HandleReceiveBlips(GiveBlipsEvent ev, EntitySessionEventArgs args)
    {
        // Exodus-begin nebula-ftl-map
        if (ev.NebulaOnly && ev.RequestedMapId != null)
        {
            _nebulaMapConfigPalette = ev.ConfigPalette;
            _nebulaMapBlips = ev.Blips;
            _lastNebulaMapUpdatedTime = _timing.CurTime;
            return;
        }
        // Exodus-end

        _configPalette = ev.ConfigPalette;
        _blips = ev.Blips;
        _hitscans = ev.HitscanLines;
        _lastUpdatedTime = _timing.CurTime;
    }

    private void RemoveBlip(BlipRemovalEvent args)
    {
        var blipid = _blips.FirstOrDefault(x => x.Uid == args.NetBlipUid);
        _blips.Remove(blipid);
    }

    public void RequestBlips(EntityUid console)
    {
        // Only request if we have a valid console
        if (!Exists(console))
            return;

        // Add request throttling to avoid network spam
        if (_timing.CurTime - _lastRequestTime < RequestThrottle)
            return;

        _lastRequestTime = _timing.CurTime;

        var netConsole = GetNetEntity(console);
        var ev = new RequestBlipsEvent(netConsole);
        RaiseNetworkEvent(ev);
    }

    // Exodus-begin nebula-ftl-map
    public void RequestNebulaMapBlips(EntityUid console, MapId mapId)
    {
        if (!Exists(console) || mapId == MapId.Nullspace)
            return;

        if (_lastNebulaMapRequest == mapId && _timing.CurTime - _lastNebulaMapRequestTime < RequestThrottle)
            return;

        _lastNebulaMapRequest = mapId;
        _lastNebulaMapRequestTime = _timing.CurTime;

        var netConsole = GetNetEntity(console);
        var ev = new RequestBlipsEvent(netConsole, (int) mapId, true);
        RaiseNetworkEvent(ev);
    }
    // Exodus-end

    /// <summary>
    /// Gets the current blips as world positions with their scale, color and shape.
    /// </summary>
    public List<BlipData> GetCurrentBlips()
    {
        // clear the cache and bail early if the data is stale
        _cachedBlipData.Clear();
        if (_timing.CurTime.TotalSeconds - _lastUpdatedTime.TotalSeconds > BlipStaleSeconds)
            return _cachedBlipData;

        // populate the cached list instead of allocating a new one each frame
        foreach (var blip in _blips)
        {
            var coord = GetCoordinates(blip.Position);

            if (!coord.IsValid(EntityManager))
                continue;

            var predictedPos = new EntityCoordinates(coord.EntityId, coord.Position + blip.Vel * (float)(_timing.CurTime - _lastUpdatedTime).TotalSeconds);

            var predictedMap = _xform.ToMapCoordinates(predictedPos);

            var config = _configPalette[blip.ConfigIndex];
            var rotation = blip.Rotation;
            // hijack our shape if we're on a grid and we want to do that
            if (_map.TryFindGridAt(predictedMap, out var grid, out _) && grid != EntityUid.Invalid)
            {
                if (blip.OnGridConfigIndex is { } gridIdx)
                    config = _configPalette[gridIdx];
                rotation += Transform(grid).LocalRotation;
            }
            var maybeGrid = grid != EntityUid.Invalid ? grid : (EntityUid?)null;

            _cachedBlipData.Add(new(blip.Uid, predictedPos, rotation, maybeGrid, config));
        }

        return _cachedBlipData;
    }

    // Exodus-begin nebula-ftl-map
    public List<BlipData> GetCurrentNebulaMapBlips()
    {
        _cachedNebulaMapBlipData.Clear();
        if (_timing.CurTime.TotalSeconds - _lastNebulaMapUpdatedTime.TotalSeconds > BlipStaleSeconds)
            return _cachedNebulaMapBlipData;

        AppendBlipData(_nebulaMapBlips, _nebulaMapConfigPalette, _cachedNebulaMapBlipData, _lastNebulaMapUpdatedTime);
        return _cachedNebulaMapBlipData;
    }
    // Exodus-end

    // Exodus-begin nebula-ftl-map
    private void AppendBlipData(List<BlipNetData> source, List<BlipConfig> palette, List<BlipData> target, TimeSpan lastUpdatedTime)
    {
        foreach (var blip in source)
        {
            if (blip.ConfigIndex >= palette.Count)
                continue;

            var coord = GetCoordinates(blip.Position);

            if (!coord.IsValid(EntityManager))
                continue;

            var predictedPos = new EntityCoordinates(coord.EntityId, coord.Position + blip.Vel * (float)(_timing.CurTime - lastUpdatedTime).TotalSeconds);
            var predictedMap = _xform.ToMapCoordinates(predictedPos);

            var config = palette[blip.ConfigIndex];
            var rotation = blip.Rotation;
            if (_map.TryFindGridAt(predictedMap, out var grid, out _) && grid != EntityUid.Invalid)
            {
                if (blip.OnGridConfigIndex is { } gridIdx && gridIdx < palette.Count)
                    config = palette[gridIdx];

                rotation += Transform(grid).LocalRotation;
            }

            var maybeGrid = grid != EntityUid.Invalid ? grid : (EntityUid?) null;
            target.Add(new(blip.Uid, predictedPos, rotation, maybeGrid, config));
        }
    }
    // Exodus-end

    /// <summary>
    /// Gets the hitscan lines to be rendered on the radar
    /// </summary>
    public List<HitscanNetData> GetHitscanLines()
    {
        if (_timing.CurTime.TotalSeconds - _lastUpdatedTime.TotalSeconds > BlipStaleSeconds)
            return new();

        return _hitscans;
    }
}

public record struct BlipData
(
    NetEntity NetUid,
    EntityCoordinates Position,
    Angle Rotation,
    EntityUid? GridUid,
    BlipConfig Config
);
