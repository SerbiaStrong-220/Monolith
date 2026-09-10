using Content.Shared._NF.Atmos.Prototypes;
using Content.Shared.Atmos;
using Robust.Shared.Prototypes;

namespace Content.Shared._Exodus.Nebula.Components;

/// <summary>
/// Defines the gas composition and extraction tuning for a nebula marker.
/// The composition is sampled from a gas-deposit prototype, but is not depleted.
/// </summary>
[RegisterComponent]
public sealed partial class NebulaGasSiphonProfileComponent : Component
{
    [DataField(required: true)]
    public ProtoId<GasDepositPrototype> Composition = default!;

    [DataField]
    public float Temperature = Atmospherics.T20C;

    [DataField]
    public float ExtractionMultiplier = 1f;
}
