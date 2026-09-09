using System.Numerics;
using Content.Shared.Atmos;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Exodus.Nebula;

/// <summary>
/// Dual-facing thruster-like siphon: while the grid moves through a dense nebula
/// with clear space along its working axis, injects gas into a connected pipe node.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NebulaGasSiphonComponent : Component
{
    public const string FilterSlotId = "filter";

    /// <summary>
    /// Tiles checked forward and backward along the configured working axis.
    /// </summary>
    [DataField]
    public int Range = 3;

    /// <summary>
    /// Number of tiles occupied by the siphon along its forward/backward axis.
    /// The free-space check starts beyond this footprint.
    /// </summary>
    [DataField]
    public int FootprintLength = 1;

    /// <summary>
    /// Local rotation from the entity's facing to the long axis checked for free space.
    /// </summary>
    [DataField]
    public Angle SpaceAxisRotation = Angle.Zero;

    /// <summary>
    /// Minimum nebula density on the parent grid.
    /// </summary>
    [DataField]
    public float MinDensity = 0.75f;

    /// <summary>
    /// Minimum linear speed (m/s) of the parent grid to operate.
    /// </summary>
    [DataField]
    public float MinSpeed = 1.5f;

    /// <summary>
    /// Parent grid speed at which the siphon reaches full speed efficiency.
    /// </summary>
    [DataField]
    public float FullSpeed = 300f;

    /// <summary>
    /// Moles of gas injected into the pipe per second at full nebula density.
    /// </summary>
    [DataField]
    public float MolesPerSecond = 8f;

    /// <summary>
    /// Max moles allowed in the connected pipe network before siphon idles.
    /// </summary>
    [DataField]
    public float MaxPipeMoles = 800f;

    /// <summary>
    /// Maximum pressure the siphon will target in the connected pipe network.
    /// </summary>
    [DataField]
    public float TargetPressure = Atmospherics.OneAtmosphere;

    [DataField]
    public string PipeNodeName = "pipe";

    [DataField]
    public EntProtoId PipePrototype = "NebulaGasSiphonGasPipe";

    [DataField]
    public Vector2 PipePosition = Vector2.Zero;

    [DataField]
    public Vector2 PipeArrowPosition = new(0.5f, 0f);

    public EntityUid? PipeEntity;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextUpdate;
}

[Serializable, NetSerializable]
public enum NebulaGasSiphonVisuals : byte
{
    FilterState,
    EmissionState,
}

[Serializable, NetSerializable]
public enum NebulaGasSiphonState : byte
{
    Empty,
    Full,
}

[Serializable, NetSerializable]
public enum NebulaGasSiphonEmissionState : byte
{
    Full = 0,
    Level1 = 1,
    Level2 = 2,
    Level3 = 3,
    Empty = 4,
}

[Serializable, NetSerializable]
public enum NebulaGasSiphonVisualLayers : byte
{
    Base,
    Emission,
}
