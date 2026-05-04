using Content.Shared._Exodus.Conveyor;
using Content.Shared.DeviceLinking;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared.Conveyor;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ConveyorComponent : Component
{
    /// <summary>
    ///     The angle to move entities by in relation to the owner's rotation.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField, AutoNetworkedField]
    public Angle Angle = Angle.Zero;

    // Exodus-conveyor-angle-begin
    /// <summary>
    ///     Angle used while in Reverse state.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Angle ReverseAngle = Angle.FromDegrees(180);
    // Exodus-conveyor-angle-end

    /// <summary>
    ///     The amount of units to move the entity by per second.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    [DataField, AutoNetworkedField]
    public float Speed = 2f;

    /// <summary>
    ///     The current state of this conveyor
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), AutoNetworkedField]
    public ConveyorState State;

    [ViewVariables, AutoNetworkedField]
    public bool Powered;

    [DataField]
    public ProtoId<SinkPortPrototype> ForwardPort = "Forward";

    [DataField]
    public ProtoId<SinkPortPrototype> ReversePort = "Reverse";

    [DataField]
    public ProtoId<SinkPortPrototype> OffPort = "Off";

    // Exodus-conveyor-speed-begin
    /// <summary>
    ///     Speed values for each tier. Keys are <see cref="ConveyorSpeedTier"/> values.
    /// </summary>
    [DataField]
    public Dictionary<ConveyorSpeedTier, float> SpeedTiers = new()
    {
        { ConveyorSpeedTier.Low, 1f },
        { ConveyorSpeedTier.Medium, 2f },
        { ConveyorSpeedTier.High, 4f },
    };

    /// <summary>
    ///     Fallback speed used when the requested tier is not defined in <see cref="SpeedTiers"/>.
    /// </summary>
    [DataField]
    public float SpeedFallback = 2f;

    /// <summary>
    ///     Currently active speed tier.
    /// </summary>
    [DataField, AutoNetworkedField]
    public ConveyorSpeedTier CurrentTier = ConveyorSpeedTier.Medium;
    // Exodus-conveyor-speed-end
}

[Serializable, NetSerializable]
public enum ConveyorVisuals : byte
{
    State
}

[Serializable, NetSerializable]
public enum ConveyorState : byte
{
    Off,
    Forward,
    Reverse
}

