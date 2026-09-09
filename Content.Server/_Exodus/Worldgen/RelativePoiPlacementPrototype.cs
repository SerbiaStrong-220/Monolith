using Content.Server._NF.GameRule;
using Content.Shared._Exodus.Nebula.Prototypes;
using Content.Shared._FarHorizons.StarSystem.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Worldgen;

/// <summary>
/// Changes placement of an existing POI, without changing its selection, map or station setup.
/// Exactly one target and one anchor must be specified. Distances are absolute world meters
/// between grid bounding-box centers (or the planet position), independent of the sector distance multiplier.
/// </summary>
[Prototype]
public sealed partial class RelativePoiPlacementPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>Ordinary POI whose selected copies will be placed relative to the anchor.</summary>
    [DataField]
    public ProtoId<PointOfInterestPrototype>? Poi { get; private set; }

    /// <summary>Alternative target from the nebula POI spawner; its nebula restrictions remain active.</summary>
    [DataField]
    public ProtoId<NebulaPoiPrototype>? NebulaPoi { get; private set; }

    /// <summary>Ordinary anchor POI. This rule does not force the anchor into the round's selection.</summary>
    [DataField]
    public ProtoId<PointOfInterestPrototype>? AnchorPoi { get; private set; }

    /// <summary>Alternative anchor from the nebula POI spawner.</summary>
    [DataField]
    public ProtoId<NebulaPoiPrototype>? AnchorNebulaPoi { get; private set; }

    /// <summary>Alternative anchor: an existing generated planet of this type on the same map.</summary>
    [DataField]
    public ProtoId<PlanetTypePrototype>? AnchorPlanet { get; private set; }

    /// <summary>Inclusive inner radius in world meters, measured between grid bounds centers.</summary>
    [DataField(required: true)]
    public float MinDistance { get; private set; }

    /// <summary>Inclusive outer radius in world meters; must be finite and at least MinDistance.</summary>
    [DataField(required: true)]
    public float MaxDistance { get; private set; }
}
