using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server._Exodus.Virology.Intelligent;

/// <summary>Shared target cooldown prevents overlapping organs from multiplying corrosion damage.</summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class RotCorrosionComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField]
    public TimeSpan NextDamage;
}
