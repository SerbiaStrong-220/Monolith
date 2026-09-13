using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Virology;

/// <summary>Two consecutive symptom-free periods, sampled anew for each infected host.</summary>
[DataDefinition, Serializable, NetSerializable]
public sealed partial class VirusIncubation
{
    /// <summary>Incubation hidden from handheld health analyzers, but not blood analysis.</summary>
    [DataField]
    public TimeSpan Hidden;

    [DataField]
    public TimeSpan? HiddenMax;

    /// <summary>Detectable incubation following the hidden period.</summary>
    [DataField]
    public TimeSpan Visible;

    [DataField]
    public TimeSpan? VisibleMax;

    public VirusIncubation Clone() => new()
    {
        Hidden = Hidden,
        HiddenMax = HiddenMax,
        Visible = Visible,
        VisibleMax = VisibleMax,
    };
}
