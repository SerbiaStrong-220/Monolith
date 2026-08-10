using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.HookahElectric;

[Serializable, NetSerializable]
public enum HookahElectricVisuals : byte
{
    Enabled,
    LeftHose,
    RightHose,
}

[Serializable, NetSerializable]
public enum HookahElectricVisualLayers : byte
{
    Base,
    LeftHose,
    RightHose,
}

