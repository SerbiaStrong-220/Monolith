// (c) Space Exodus Team - MPL-2.0 with CLA
// Authors: Lokilife
using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Exodus.Tailed;

/// <summary>
/// When given to an entity, creates X tailed entities that try to follow the entity with the component.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class TailedEntityComponent : Component
{
    /// <summary>
    /// amount of entities in between the tail and the head
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Amount = 3;

    /// <summary>
    /// the entity/entities that should be spawned after the head
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public EntProtoId Prototype;

    /// <summary>
    /// How much space between entities
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Spacing = 1f;

    // oh my gosh, it so many fucking variables, to make it move smooth
    [DataField, AutoNetworkedField]
    public float Stiffness = 50.0f;

    [DataField, AutoNetworkedField]
    public float Damping = 2.0f;

    [DataField, AutoNetworkedField]
    public float MaxLengthMultiplier = 1.05f;

    [DataField, AutoNetworkedField]
    public float MinLengthMultiplier = 0.95f;

    [DataField, AutoNetworkedField]
    public bool EnableRotationControl = true;

    [DataField, AutoNetworkedField]
    public float RotationLerpSpeed = 10f;

    [DataField, AutoNetworkedField]
    public Vector2 AnchorAOffset = new(-0.15f, 0);

    [DataField, AutoNetworkedField]
    public Vector2 AnchorBOffset = new(0.15f, 0);

    [DataField, AutoNetworkedField]
    public Angle RotationModifier = Angle.FromDegrees(-90);

    [DataField, AutoNetworkedField]
    public float FollowSharpness = 5f;

    [DataField, AutoNetworkedField]
    public float MaxSegmentSpeed = 10f;

    [DataField, AutoNetworkedField]
    public float VelocitySmoothing = 10f;

    [DataField, AutoNetworkedField]
    public float SpacingTolerance = 0.2f;

    /// <summary>
    /// List of tail segments
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<EntityUid> TailSegments = new();
}
