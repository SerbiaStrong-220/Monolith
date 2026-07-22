using Robust.Shared.Map;

namespace Content.Server._Exodus.Nebula.Generation;

/// <summary>
/// Coordinates Exodus nebula round-start generation after NF has finished placing stations
/// and POIs that regular nebula generation must avoid.
/// </summary>
public sealed partial class NebulaRoundstartGenerationSystem : EntitySystem
{
    [Dependency] private NebulaGenerationSystem _nebulaGeneration = default!;
    [Dependency] private DeathZoneGenerationSystem _deathZoneGeneration = default!;
    [Dependency] private NebulaPoiSpawnSystem _poiSpawn = default!;

    private ISawmill _sawmill = Logger.GetSawmill("nebula");

    public bool GenerateRoundstartContent(MapId mapId)
    {
        if (!_nebulaGeneration.Generate(mapId))
        {
            _sawmill.Warning($"Skipping remaining nebula round-start generation because blob generation failed on map {mapId}.");
            return false;
        }

        if (!_deathZoneGeneration.Generate(mapId))
        {
            _sawmill.Warning($"Skipping nebula POI generation because world-end generation failed on map {mapId}.");
            return false;
        }

        if (_poiSpawn.TrySpawnAllPois(mapId))
            return true;

        _sawmill.Warning($"Nebula POI generation failed on map {mapId}.");
        return false;
    }
}
