using Content.Shared.Chemistry.Components;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._Exodus.Fluids;

/// <summary>
/// Releases a configured foam solution after preparing the device in hand.
/// Uses LimitedCharges and AutoRecharge when a finite, regenerating reservoir is needed.
/// </summary>
[RegisterComponent]
public sealed partial class FoamSprayerComponent : Component
{
    /// <summary>Time the operator must spend preparing each spray.</summary>
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(20);

    /// <summary>Prototype of the spreading foam.</summary>
    [DataField]
    public EntProtoId FoamPrototype = "Foam";

    /// <summary>Lifetime of each discharge, including its spread.</summary>
    [DataField]
    public TimeSpan FoamDuration = TimeSpan.FromSeconds(15);

    /// <summary>Spread budget passed to the smoke system; this is not a radius.</summary>
    [DataField]
    public int SpreadAmount = 100;

    /// <summary>Solution copied into each discharge.</summary>
    [DataField(required: true)]
    public Solution Solution = new();

    /// <summary>Sound played when the foam is released.</summary>
    [DataField]
    public SoundSpecifier SpraySound = new SoundPathSpecifier("/Audio/Effects/spray2.ogg");
}
