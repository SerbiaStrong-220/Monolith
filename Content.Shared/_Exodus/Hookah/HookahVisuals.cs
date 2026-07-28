using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Hookah;

[Serializable, NetSerializable]
public enum HookahVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum HookahVisualState : byte
{
    Unlit,
    UnlitNoHose,
    CoalUnlit,
    CoalUnlitNoHose,
    CoalLit,
    CoalLitNoHose,
}

