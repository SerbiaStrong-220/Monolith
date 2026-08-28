using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._Exodus.Nebula;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NebulaGasSiphonFilterComponent : Component
{
    public const int RemainingStageCount = 20;

    [DataField]
    public float Capacity = 2500f;

    [DataField]
    public float Remaining = -1f;

    /// <summary>
    /// Quantized remaining reserve in 5% steps for networked UI and visuals.
    /// </summary>
    [AutoNetworkedField]
    public byte RemainingStage;

    [DataField]
    public float ConsumptionPerMole = 0.25f;
}

[Serializable, NetSerializable]
public enum NebulaGasSiphonFilterVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum NebulaGasSiphonFilterState : byte
{
    Intact,
    Depleted,
}
