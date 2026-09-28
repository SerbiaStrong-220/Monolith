using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Virology.Intelligent;

[Prototype]
public sealed partial class RotBuildingPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public EntProtoId Entity;
    [DataField(required: true)] public LocId Name;
    [DataField] public LocId Description;
    [DataField] public float Cost = 10;
    [DataField] public TimeSpan Duration = TimeSpan.FromSeconds(4);
    [DataField] public Vector2i Size = Vector2i.One;
    [DataField] public bool Expansion;
    [DataField] public bool Wall;
    [DataField] public bool RequiresExhaust;
    [DataField] public float ConversionSecondsPerDamage = 0.015f;
}

[Serializable, NetSerializable]
public enum RotOrganVisuals : byte
{
    Connected,
    Growth,
    Nutrition,
    Connections,
}
