using Content.Server.GameTicking;
using Content.Shared._Exodus.Shuttles; // Exodus
using Content.Shared._Exodus.StarSystem; // Exodus
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Robust.Server.GameObjects;
using Robust.Server.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._FarHorizons.StarSystem;

public sealed partial class StarSystemMapSystem : SharedStarSystemMapSystem
{
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private MapSystem _map = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private PvsOverrideSystem _pvs = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PostGameMapLoad>(OnPostMapLoad);
    }

    private void OnPostMapLoad(PostGameMapLoad ev)
    {
        if (!_map.TryGetMap(ev.Map, out var mapUid)) return;
        var comp = EnsureComp<StarSystemMapComponent>(mapUid.Value);

        if (comp.System is { } system)
            SetSystem((mapUid.Value, comp), system);
    }

    public void SetSystem(Entity<StarSystemMapComponent> ent, ProtoId<StarSystemPrototype> system)
    {
        // Exodus: loading another grid or restarting preset rules must not duplicate the same system.
        if (ent.Comp.System == system && ent.Comp.StarSystem != null)
            return;

        ent.Comp.System = system;
        ent.Comp.StarSystem = BuildPlanetarySystem(system);
        Dirty(ent);

        EnsureComp<StarLightComponent>(ent);

        SpawnEntities(ent);
    }

    private void SpawnEntities(Entity<StarSystemMapComponent> ent)
    {
        if (ent.Comp.StarSystem == null)
            return;

        if (_protoMan.TryIndex<EntityPrototype>(Star.STAR_ENTITY, out var starEnt))
        {
            var coords = new EntityCoordinates(ent, ent.Comp.StarSystem.Star.Position);
            var spawned = SpawnAtPosition(starEnt.ID, coords);
            // Exodus: named stellar objects accept localization keys; upstream literal names remain supported.
            var name = ent.Comp.StarSystem.Star.Name;
            _metadata.SetEntityName(spawned, Loc.TryGetString(name, out var localizedName) ? localizedName : name);
            _pvs.AddGlobalOverride(spawned);
        }

        if (_protoMan.TryIndex<EntityPrototype>(Planet.PLANET_ENTITY, out var planetEnt))
        {
            foreach (var planet in ent.Comp.StarSystem.Planets)
            {
                var planetCoords = new EntityCoordinates(ent, planet.Position);
                var spawnedPlanet = SpawnAtPosition(planetEnt.ID, planetCoords);
                // Exodus-begin localized planet names and configurable classification labels.
                var marker = EnsureComp<PlanetMarkerComponent>(spawnedPlanet);
                marker.Planet = planet.Prototype;
                marker.RadarRange = planet.RadarRange;
                Dirty(spawnedPlanet, marker);
                _metadata.SetEntityName(spawnedPlanet, Loc.TryGetString(planet.Name, out var localizedName) ? localizedName : planet.Name);
                if (planet.RadarLabel is { } label)
                {
                    var affiliation = EnsureComp<IffAffiliationComponent>(spawnedPlanet);
                    affiliation.Mode = IffAffiliationMode.FixedLabel;
                    affiliation.Label = label;
                    Dirty(spawnedPlanet, affiliation);
                }
                // Exodus-end
                _pvs.AddGlobalOverride(spawnedPlanet);
            }
        }
    }
}
