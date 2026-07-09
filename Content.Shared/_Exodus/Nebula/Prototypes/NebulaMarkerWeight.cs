using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Prototypes;

/// <summary>
/// One entry in <see cref="NebulaGenerationConfigPrototype.Markers"/>: a marker prototype id
/// plus a weight for weighted random selection. Higher weight = more frequent.
/// Weight 0 disables the marker; negative weights are clamped to 0.
/// </summary>
[DataDefinition]
public sealed partial class NebulaMarkerWeight
{
    [DataField(required: true)]
    public EntProtoId Proto;

    [DataField]
    public float Weight = 1f;
}
