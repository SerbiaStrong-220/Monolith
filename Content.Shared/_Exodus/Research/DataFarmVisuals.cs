// (c) Space Exodus Team - EXDS-RL
// Authors: DarkBanOne

using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Research.Visuals;


[Serializable, NetSerializable]
public enum DataFarmVisuals : byte
{
    State
}

[Serializable, NetSerializable]
public enum DataFarmVisualLayers : byte
{
    State,
    Light
}

[Serializable, NetSerializable]
public enum DataFarmState : byte
{
    Off,
    Process,
    Normal,
    Warning,
    Destruct
}
