using Content.Server._FarHorizons.StarSystem;
using Content.Shared._Exodus.CCVar;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.StarSystem;

/// <summary>
/// Prepares the main sector independently of game presets, before POIs and nebulas are generated.
/// Additional expedition and shuttle maps are deliberately not initialized here.
/// </summary>
public sealed class DefaultStarSystemSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _configuration = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedMapSystem _maps = default!;
    [Dependency] private readonly StarSystemMapSystem _stars = default!;

    public void EnsureDefaultSystem(MapId map)
    {
        if (!_maps.TryGetMap(map, out var mapUid))
            return;

        var component = CompOrNull<StarSystemMapComponent>(mapUid.Value);
        if (component?.StarSystem != null)
            return;

        var configured = component?.System?.Id ?? _configuration.GetCVar(EXCVars.DefaultStarSystem);
        if (string.IsNullOrWhiteSpace(configured))
            return;

        if (!_prototypes.TryIndex<StarSystemPrototype>(configured, out var prototype))
        {
            Log.Error($"Cannot generate the default star system: unknown prototype {configured}.");
            return;
        }

        component ??= EnsureComp<StarSystemMapComponent>(mapUid.Value);
        _stars.SetSystem((mapUid.Value, component), prototype.ID);
    }
}
