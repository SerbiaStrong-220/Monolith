using Content.Shared._FarHorizons.StarSystem.Helpers;
using Robust.Shared.Prototypes;

namespace Content.Shared._FarHorizons.StarSystem.Prototypes;

[Prototype]
public sealed partial class PlanetTypePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public string Name = default!;
    // Exodus: optional localization key for the planet classification shown instead of IFF affiliation.
    [DataField] public string? RadarLabel;
    /// <summary>Exodus: ordinary radar visibility distance in meters, independent of FTL map visibility.</summary>
    [DataField] public float RadarRange = 10000f;
    [DataField(required: true)] public string Shader = default!;
    [DataField(required: true)] public float EarthMass;
    [DataField] public float Rotation;
    [DataField(required: true)] public ProtoId<PlanetPalettePrototype> Palette;
    [DataField] public float HueShift;
    [DataField] public float SaturationShift;
    [DataField] public ProtoId<PlanetaryAtmosphereTypePrototype>? Atmosphere;
    [DataField] public ProtoId<PlanetaryLiquidTypePrototype>? Liquid;
    [DataField] public ProtoId<PlanetaryRingsTypePrototype>? Rings;
    [DataField] public PlanetCustomValues CustomData = new();
}
