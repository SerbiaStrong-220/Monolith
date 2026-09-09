using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.StarSystem;

/// <summary>
/// Identifies a generated planet for relative POI placement and navigation displays.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class PlanetMarkerComponent : Component
{
    /// <summary>The planet type used to generate this marker.</summary>
    [DataField, AutoNetworkedField]
    public ProtoId<PlanetTypePrototype>? Planet;

    /// <summary>Maximum distance in meters at which the ordinary radar displays this planet.</summary>
    [DataField, AutoNetworkedField]
    public float RadarRange = 10000f;
}
