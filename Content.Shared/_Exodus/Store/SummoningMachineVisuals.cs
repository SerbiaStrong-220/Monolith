using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Store;

[Serializable, NetSerializable]
public enum SummoningMachineVisuals : byte
{
    State
}

[Serializable, NetSerializable]
public enum SummoningMachineVisualState : byte
{
    Inactive,
    Idle,
    Working
}

[Serializable, NetSerializable]
public enum SummoningMachineVisualLayers : byte
{
    Base,
    State,
    Emission
}
