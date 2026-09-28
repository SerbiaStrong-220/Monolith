using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Exodus.Virology;

/// <summary>An active infection, distinct from a transmissible sample which starts a new infection.</summary>
[DataDefinition]
public sealed partial class VirusSavedState
{
    [DataField] public VirusDescriptor Strain = new();
    [DataField] public Dictionary<ProtoId<VirusSymptomPrototype>, VirusSymptomState> Symptoms = [];
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan? IncubationEndsAt;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan? HiddenUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan? SuppressedUntil;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))] public TimeSpan NextEffect;
}
