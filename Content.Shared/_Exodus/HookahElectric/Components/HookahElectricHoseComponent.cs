using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.HookahElectric.Components;

[RegisterComponent]
public sealed partial class HookahElectricHoseComponent : Component
{
    [DataField]
    public HookahElectricHoseSide Side = HookahElectricHoseSide.Left;
}

[Serializable, NetSerializable]
public enum HookahElectricHoseSide : byte
{
    Left,
    Right,
}

